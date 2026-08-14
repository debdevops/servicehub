using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Periodically recomputes Evidence-Derived Trust Scoring (roadmap §8.10, Phase C) for every
/// signature with replay evidence and logs a fleet-level summary — "learning" as recomputed
/// aggregation over the same scheduled-job pattern as <see cref="RecoveryAgeingWorker"/>, not a
/// Learning Engine subsystem (roadmap §13). Writes nothing: no new table exists for this phase,
/// and no <c>AutonomyGrant</c> is created — that write path belongs to Phase D's extension of
/// this same worker.
/// </summary>
/// <remarks>
/// Purely a query-driven sweep over durable ledger state, like <see cref="RecoveryAgeingWorker"/>
/// and <see cref="RecoveryVerificationWorker"/>: a restart loses nothing, because nothing is held
/// in memory between sweeps.
/// </remarks>
public sealed class AutonomyEvaluationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AutonomyEvaluationWorker> _logger;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(45);
    private const int DefaultSweepIntervalSeconds = 3600;

    private readonly TimeSpan _sweepInterval;

    /// <summary>Initializes a new instance of the <see cref="AutonomyEvaluationWorker"/> class.</summary>
    /// <param name="serviceProvider">Root service provider for per-sweep-cycle scope creation.</param>
    /// <param name="configuration">
    /// Application configuration — reads <c>RecoveryEvidence:AutonomyEvaluationSweepIntervalSeconds</c>
    /// (default 3600, clamped to [60, 86400]).
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public AutonomyEvaluationWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<AutonomyEvaluationWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _sweepInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("RecoveryEvidence:AutonomyEvaluationSweepIntervalSeconds", DefaultSweepIntervalSeconds),
            60, 86400));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Autonomy Evaluation Worker starting. Sweep interval: {Interval}s", _sweepInterval.TotalSeconds);

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
                        "Autonomy Evaluation Worker could not list active namespaces: {Error}",
                        namespacesResult.Error.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Autonomy Evaluation Worker sweep cycle");
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

        _logger.LogInformation("Autonomy Evaluation Worker stopped");
    }

    /// <summary>Sweeps one owner's signatures with replay evidence. Internal (rather than
    /// private) so tests can drive a single sweep cycle directly instead of waiting on
    /// <see cref="ExecuteAsync"/>'s timer loop.</summary>
    internal async Task SweepOwnerAsync(IServiceProvider services, string ownerId, CancellationToken cancellationToken)
    {
        var recoveryLedger = services.GetRequiredService<IRecoveryLedger>();
        var trustScoring = services.GetRequiredService<IRecoveryTrustScoringService>();

        var signatureHashes = await recoveryLedger.GetDistinctSignatureHashesAsync(
            ownerId, RecoveryOperationKind.Replay, cancellationToken);

        if (signatureHashes.Count == 0)
        {
            return;
        }

        var l4Eligible = 0;
        var l5Eligible = 0;

        foreach (var signatureHash in signatureHashes)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = await trustScoring.EvaluateAsync(
                ownerId, signatureHash, RecoveryOperationKind.Replay, cancellationToken);

            if (result.IsFailure)
            {
                _logger.LogDebug(
                    "Skipped trust evaluation for owner {OwnerId} signature {SignatureHash}: {Error}",
                    ownerId, signatureHash, result.Error.Message);
                continue;
            }

            if (result.Value.MeetsL4SampleAndRate)
            {
                l4Eligible++;
            }

            if (result.Value.MeetsL5SampleAndRate)
            {
                l5Eligible++;
            }
        }

        _logger.LogInformation(
            "Autonomy Evaluation Worker: owner {OwnerId} — {SignatureCount} signature(s) with replay evidence, " +
            "{L4Count} meet the L3→L4 sample/rate threshold, {L5Count} meet the L4→L5 sample/rate threshold " +
            "(sample/rate only — no AutonomyGrant is written this phase)",
            ownerId, signatureHashes.Count, l4Eligible, l5Eligible);
    }
}
