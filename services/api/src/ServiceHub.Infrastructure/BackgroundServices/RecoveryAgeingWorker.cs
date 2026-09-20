using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Flags, then expires, non-terminal <see cref="RecoveryLedgerEntry"/> rows that have sat open
/// past the ageing threshold — the falsifiable form of "nothing is silently lost" (roadmap §7.2):
/// every such entry is guaranteed to surface on the ageing report (<c>GetAgeingAsync</c> already
/// returns every non-terminal entry, unconditionally) before this worker ever moves it further.
/// </summary>
/// <remarks>
/// <para>
/// Two-pass by design, not two-worker: on the sweep where an entry first crosses the threshold,
/// it is only flagged (<see cref="IRecoveryLedger.FlagAgeingAsync"/>) — the state and terminality
/// are untouched, so it keeps appearing on the ageing report exactly as it did before. Only on a
/// <em>later</em> sweep, once <see cref="IRecoveryLedger.HasAgeingFlagAsync"/> confirms the flag
/// is already recorded, does this worker call <see cref="IRecoveryLedger.ExpireEntryAsync"/>,
/// which itself re-checks that the flag event is the entry's most recent one before allowing the
/// transition — the structural enforcement of "Expired reachable only through a transition whose
/// preceding event is AgeingFlagged".
/// </para>
/// <para>
/// Purely a query-driven sweep over durable state, like <see cref="RecoveryVerificationWorker"/>:
/// a restart loses nothing to lose, because nothing is held in memory. Concurrent or duplicate
/// sweeps are safe because both ledger calls are idempotent — <c>FlagAgeingAsync</c> no-ops once
/// an <c>AgeingFlagged</c> event exists, and <c>ExpireEntryAsync</c> no-ops (returns a Conflict
/// the worker logs and moves past) once the entry is no longer non-terminal or its last event is
/// no longer exactly <c>AgeingFlagged</c>. An entry that resolves itself normally (e.g. a delayed
/// verification-window close) between two sweeps simply stops appearing in
/// <c>GetAgeingAsync</c>'s non-terminal set and is never touched again by this worker.
/// </para>
/// </remarks>
public sealed class RecoveryAgeingWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecoveryAgeingWorker> _logger;
    private readonly IWorkerHeartbeatStore? _heartbeatStore;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(30);
    private const int DefaultSweepIntervalSeconds = 3600;
    private const int DefaultThresholdDays = 7;
    private const int DefaultMaxAgeingBatchSize = 1000;

    private readonly TimeSpan _sweepInterval;
    private readonly int _thresholdDays;
    private readonly int _maxAgeingBatchSize;

    /// <summary>Initializes a new instance of the <see cref="RecoveryAgeingWorker"/> class.</summary>
    /// <param name="serviceProvider">Root service provider for per-sweep-cycle scope creation.</param>
    /// <param name="configuration">
    /// Application configuration — reads <c>RecoveryEvidence:AgeingSweepIntervalSeconds</c>
    /// (default 3600, clamped to [60, 86400]), <c>RecoveryEvidence:AgeingThresholdDays</c>
    /// (default 7, clamped to [1, 3650]), and <c>RecoveryEvidence:MaxAgeingBatchSize</c> (default
    /// 1000, clamped to [1, 100000]) — the per-sweep cap on how many of an owner's non-terminal
    /// entries this worker fetches at once. Oldest entries are fetched first, so a backlog beyond
    /// the cap is picked up on a later sweep as older entries ahead of it terminalize, never
    /// starved.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public RecoveryAgeingWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<RecoveryAgeingWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _sweepInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("RecoveryEvidence:AgeingSweepIntervalSeconds", DefaultSweepIntervalSeconds),
            60, 86400));
        _thresholdDays = Math.Clamp(
            configuration.GetValue("RecoveryEvidence:AgeingThresholdDays", DefaultThresholdDays),
            1, 3650);
        _maxAgeingBatchSize = Math.Clamp(
            configuration.GetValue("RecoveryEvidence:MaxAgeingBatchSize", DefaultMaxAgeingBatchSize),
            1, 100_000);

        // Optional: GetService (not GetRequiredService) so tests that build a root provider
        // without registering it keep working — heartbeat recording degrades to a no-op instead.
        _heartbeatStore = serviceProvider.GetService<IWorkerHeartbeatStore>();
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Recovery Ageing Worker starting. Sweep interval: {Interval}s, threshold: {ThresholdDays}d",
            _sweepInterval.TotalSeconds, _thresholdDays);

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
                    // One sweep per distinct owner — GetAgeingAsync already returns every
                    // non-terminal entry for an owner regardless of which namespace it belongs
                    // to, so a per-namespace sweep would just reprocess the same owner's entries.
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
                        "Recovery Ageing Worker could not list active namespaces: {Error}",
                        namespacesResult.Error.Message);
                }

                _heartbeatStore?.RecordHeartbeat(nameof(RecoveryAgeingWorker), _sweepInterval);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Recovery Ageing Worker sweep cycle");
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

        _logger.LogInformation("Recovery Ageing Worker stopped");
    }

    /// <summary>Sweeps one owner's non-terminal entries. Internal (rather than private) so tests
    /// can drive a single sweep cycle directly instead of waiting on <see cref="ExecuteAsync"/>'s
    /// timer loop.</summary>
    internal async Task SweepOwnerAsync(IServiceProvider services, string ownerId, CancellationToken cancellationToken)
    {
        var recoveryLedger = services.GetRequiredService<IRecoveryLedger>();

        var nonTerminal = await recoveryLedger.GetAgeingAsync(ownerId, _maxAgeingBatchSize, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var threshold = now.AddDays(-_thresholdDays);

        var due = nonTerminal.Where(e => e.BegunAt <= threshold);

        var actor = ActorIdentityResolver.ResolveSystemActor("RecoveryAgeingWorker");

        foreach (var entry in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var alreadyFlagged = await recoveryLedger.HasAgeingFlagAsync(entry.Id, ownerId, cancellationToken);

            if (alreadyFlagged)
            {
                var expireResult = await recoveryLedger.ExpireEntryAsync(entry.Id, ownerId, actor, cancellationToken);
                if (expireResult.IsFailure)
                {
                    // Most commonly a lost race against a normal resolution path closing the
                    // entry between the query above and this call — harmless, not an error.
                    _logger.LogDebug(
                        "Skipped expiring recovery ledger entry {EntryId}: {Error}",
                        entry.Id, expireResult.Error.Message);
                }
                else
                {
                    _logger.LogInformation(
                        "Recovery ledger entry {EntryId} expired after ageing threshold ({ThresholdDays}d)",
                        entry.Id, _thresholdDays);
                }

                continue;
            }

            var ageInDays = (int)(now - entry.BegunAt).TotalDays;
            var flagResult = await recoveryLedger.FlagAgeingAsync(entry.Id, ownerId, actor, ageInDays, cancellationToken);
            if (flagResult.IsFailure)
            {
                _logger.LogDebug(
                    "Skipped flagging recovery ledger entry {EntryId}: {Error}",
                    entry.Id, flagResult.Error.Message);
            }
            else
            {
                _logger.LogInformation(
                    "Recovery ledger entry {EntryId} flagged for ageing ({AgeInDays}d open)",
                    entry.Id, ageInDays);
            }
        }
    }
}
