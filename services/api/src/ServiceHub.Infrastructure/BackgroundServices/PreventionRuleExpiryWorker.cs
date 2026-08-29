using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Revokes promoted P5 <c>PreventionRule</c>s once their own <c>RuleExpiresAt</c> has passed
/// without reconfirmation (<c>PREVENTION-RULE-DESIGN-2026-08-29.md</c> §9) — sibling to
/// <see cref="PlaybookExpiryWorker"/>, but a distinct sweep: a <c>PreventionRuleProposal</c>'s
/// expiry-that-matters lives inside its opaque <c>ProposalJson</c> (<c>RuleExpiresAt</c>), not the
/// entry's own <c>ExpiresAt</c> column, which stops mattering the moment the entry reaches the
/// terminal <c>Approved</c> state and drops out of <see cref="IPlaybookLedger.GetDueForExpiryAsync"/>'s
/// non-terminal scan. A rule nobody has re-confirmed lapses on its own — a deliberate safety
/// property: a stale, forgotten rule silently stops firing rather than silently keeps firing
/// forever.
/// </summary>
public sealed class PreventionRuleExpiryWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(50);
    private const int DefaultSweepIntervalSeconds = 3600;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PreventionRuleExpiryWorker> _logger;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;

    private readonly TimeSpan _sweepInterval;

    /// <summary>Initializes a new instance of the <see cref="PreventionRuleExpiryWorker"/> class.</summary>
    /// <param name="serviceProvider">Root service provider for per-sweep-cycle scope creation.</param>
    /// <param name="configuration">Application configuration — reads
    /// <c>PreventionRule:ExpirySweepIntervalSeconds</c> (default 3600, clamped to [60, 86400]).</param>
    /// <param name="logger">Logger instance.</param>
    public PreventionRuleExpiryWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<PreventionRuleExpiryWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _sweepInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("PreventionRule:ExpirySweepIntervalSeconds", DefaultSweepIntervalSeconds),
            60, 86400));

        // Optional: GetService (not GetRequiredService) so tests that build a root provider
        // without registering it keep working — heartbeat recording degrades to a no-op instead.
        _heartbeatStore = serviceProvider.GetService<IWorkerHeartbeatStore>();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Prevention Rule Expiry Worker starting. Sweep interval: {Interval}s", _sweepInterval.TotalSeconds);

        try
        {
            await Task.Delay(InitialDelay, stoppingToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var namespaceRepo = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();
                var evaluationService = scope.ServiceProvider.GetRequiredService<IPreventionRuleEvaluationService>();

                var namespacesResult = await namespaceRepo.GetActiveAsync(stoppingToken);
                if (namespacesResult.IsSuccess)
                {
                    // One sweep per distinct owner — SweepExpiredRulesAsync already covers every
                    // namespace for that owner in one query, mirroring PlaybookExpiryWorker's own
                    // per-owner (not per-namespace) sweep for the same reason.
                    var ownerIds = namespacesResult.Value
                        .Select(n => n.OwnerId)
                        .Distinct(StringComparer.Ordinal);

                    foreach (var ownerId in ownerIds)
                    {
                        var revokedCount = await evaluationService.SweepExpiredRulesAsync(
                            ownerId, DateTimeOffset.UtcNow, stoppingToken);

                        if (revokedCount > 0)
                        {
                            _logger.LogInformation(
                                "Prevention Rule Expiry Worker revoked {Count} expired rule(s) for owner {OwnerId}",
                                revokedCount, ownerId);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Prevention Rule Expiry Worker could not list active namespaces: {Error}",
                        namespacesResult.Error.Message);
                }

                _heartbeatStore?.RecordHeartbeat(nameof(PreventionRuleExpiryWorker), _sweepInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Prevention Rule Expiry Worker sweep cycle");
            }

            try
            {
                await Task.Delay(_sweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Prevention Rule Expiry Worker stopped");
    }
}
