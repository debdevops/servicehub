using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Expires non-terminal <see cref="Core.Entities.PlaybookEntry"/> rows once their
/// <see cref="Core.Entities.PlaybookEntry.ExpiresAt"/> has passed without a human decision
/// (roadmap §5.6, "Required API/UI surfaces… the only new worker this item needs; no new provider
/// calls, no new external I/O"). Simpler than <see cref="RecoveryAgeingWorker"/>'s flag-then-expire
/// two-pass sweep: a Playbook entry's expiry moment is fixed at proposal time
/// (<see cref="IPlaybookLedger.ProposeAsync"/>'s <c>ExpiresAfter</c>), so there is nothing to flag
/// first — a single pass straight to <see cref="IPlaybookLedger.ExpireAsync"/> is sufficient, and
/// that call is itself idempotent (a no-op once the entry is already terminal).
/// </summary>
public sealed class PlaybookExpiryWorker : BackgroundService
{
    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(40);
    private const int DefaultSweepIntervalSeconds = 3600;
    private const int DefaultMaxExpiryBatchSize = 1000;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PlaybookExpiryWorker> _logger;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;

    private readonly TimeSpan _sweepInterval;
    private readonly int _maxExpiryBatchSize;

    /// <summary>Initializes a new instance of the <see cref="PlaybookExpiryWorker"/> class.</summary>
    /// <param name="serviceProvider">Root service provider for per-sweep-cycle scope creation.</param>
    /// <param name="configuration">
    /// Application configuration — reads <c>Playbook:ExpirySweepIntervalSeconds</c> (default 3600,
    /// clamped to [60, 86400]) and <c>Playbook:MaxExpiryBatchSize</c> (default 1000, clamped to
    /// [1, 100000]) — the per-owner, per-sweep cap on how many due entries this worker fetches at
    /// once. Oldest-expiring entries are fetched first, so a backlog beyond the cap is picked up
    /// on a later sweep, never starved.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public PlaybookExpiryWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<PlaybookExpiryWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _sweepInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("Playbook:ExpirySweepIntervalSeconds", DefaultSweepIntervalSeconds),
            60, 86400));
        _maxExpiryBatchSize = Math.Clamp(
            configuration.GetValue("Playbook:MaxExpiryBatchSize", DefaultMaxExpiryBatchSize),
            1, 100_000);

        // Optional: GetService (not GetRequiredService) so tests that build a root provider
        // without registering it keep working — heartbeat recording degrades to a no-op instead.
        _heartbeatStore = serviceProvider.GetService<IWorkerHeartbeatStore>();
    }

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Playbook Expiry Worker starting. Sweep interval: {Interval}s", _sweepInterval.TotalSeconds);

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

                var namespacesResult = await namespaceRepo.GetActiveAsync(stoppingToken);
                if (namespacesResult.IsSuccess)
                {
                    // One sweep per distinct owner — GetDueForExpiryAsync already returns every
                    // due entry for an owner regardless of which namespace it belongs to (and
                    // some entries, e.g. correlation hypotheses, carry no namespace at all), so a
                    // per-namespace sweep would just reprocess the same owner's entries.
                    var ownerIds = namespacesResult.Value
                        .Select(n => n.OwnerId)
                        .Distinct(StringComparer.Ordinal);

                    foreach (var ownerId in ownerIds)
                    {
                        await SweepOwnerAsync(scope.ServiceProvider, ownerId, stoppingToken);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Playbook Expiry Worker could not list active namespaces: {Error}",
                        namespacesResult.Error.Message);
                }

                _heartbeatStore?.RecordHeartbeat(nameof(PlaybookExpiryWorker), _sweepInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Playbook Expiry Worker sweep cycle");
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

        _logger.LogInformation("Playbook Expiry Worker stopped");
    }

    /// <summary>Sweeps one owner's due-for-expiry entries. Internal (rather than private) so tests
    /// can drive a single sweep cycle directly instead of waiting on <see cref="ExecuteAsync"/>'s
    /// timer loop.</summary>
    internal async Task SweepOwnerAsync(IServiceProvider services, string ownerId, CancellationToken cancellationToken)
    {
        var playbookLedger = services.GetRequiredService<IPlaybookLedger>();

        var dueResult = await playbookLedger.GetDueForExpiryAsync(
            ownerId, DateTimeOffset.UtcNow, _maxExpiryBatchSize, cancellationToken);

        if (dueResult.IsFailure)
        {
            _logger.LogWarning(
                "Playbook Expiry Worker could not query due entries for owner {OwnerId}: {Error}",
                ownerId, dueResult.Error.Message);
            return;
        }

        foreach (var entry in dueResult.Value)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var expireResult = await playbookLedger.ExpireAsync(entry.Id, ownerId, cancellationToken);
            if (expireResult.IsFailure)
            {
                // Most commonly a lost race against a human dispositioning the entry between the
                // query above and this call — harmless, not an error.
                _logger.LogDebug(
                    "Skipped expiring Playbook Ledger entry {EntryId}: {Error}",
                    entry.Id, expireResult.Error.Message);
            }
            else
            {
                _logger.LogInformation(
                    "Playbook Ledger entry {EntryId} ({ProposalKind}) expired without a human decision",
                    entry.Id, entry.ProposalKind);
            }
        }
    }
}
