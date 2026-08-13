using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Closes out the post-replay observation window opened by <c>RecoveryLedgerService.RecordExecutionAsync</c>
/// (see <see cref="RecoveryEntryState.Observing"/>). A recurrence observed <em>within</em> the
/// window is recorded immediately by <c>DlqMonitorService</c>'s scan-time recurrence hook; this
/// worker only handles the window-close verdict for entries that never saw one — "did not
/// return" — which requires proof the DLQ was actually watched the whole time (§8.4 of the
/// Recovery Evidence Ledger verification model).
/// </summary>
/// <remarks>
/// <para>
/// Purely a query-driven sweep over durable state (<see cref="RecoveryLedgerEntry.ObservationWindowEndsAt"/>):
/// a process restart loses nothing, because there is nothing held in memory to lose — the next
/// sweep after startup simply finds the same due entries a fresh process would. Concurrent or
/// duplicate sweeps (multiple instances, or two sweeps racing after a slow cycle) are safe
/// because <c>RecoveryLedgerService.RecordObservationAsync</c> rejects a second transition once
/// an entry has left <see cref="RecoveryEntryState.Observing"/> — a losing sweep's write is a
/// harmless no-op, not a corruption.
/// </para>
/// <para>
/// Coverage is decided from <see cref="ProviderCapabilities.CanProveDlqAbsence"/> — a static,
/// conservative, per-provider fact (Azure only) rather than a dynamic per-cycle scan-coverage
/// log, so this ships without new schema. It also requires the entry's namespace to still be
/// registered and its provider still enabled; either missing means ServiceHub cannot have been
/// watching, so the verdict is <see cref="RecoveryObservationOutcome.ObservationUnavailable"/>,
/// never <see cref="RecoveryObservationOutcome.NoRecurrenceObserved"/>.
/// </para>
/// </remarks>
public sealed class RecoveryVerificationWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<RecoveryVerificationWorker> _logger;

    private static readonly TimeSpan InitialDelay = TimeSpan.FromSeconds(15);
    private const int DefaultSweepIntervalSeconds = 60;

    private readonly TimeSpan _sweepInterval;

    /// <summary>Initializes a new instance of the <see cref="RecoveryVerificationWorker"/> class.</summary>
    /// <param name="serviceProvider">Root service provider for per-sweep-cycle scope creation.</param>
    /// <param name="configuration">
    /// Application configuration — reads <c>RecoveryEvidence:VerificationSweepIntervalSeconds</c>
    /// (default 60, clamped to [10, 3600]).
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public RecoveryVerificationWorker(
        IServiceProvider serviceProvider,
        IConfiguration configuration,
        ILogger<RecoveryVerificationWorker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        _sweepInterval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("RecoveryEvidence:VerificationSweepIntervalSeconds", DefaultSweepIntervalSeconds),
            10, 3600));
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Recovery Verification Worker starting. Sweep interval: {Interval}s", _sweepInterval.TotalSeconds);

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
                    // One sweep per distinct owner, not per namespace — GetAgeingAsync already
                    // returns every non-terminal entry for an owner regardless of which of their
                    // namespaces it belongs to, so sweeping per-namespace would just reprocess
                    // the same owner's entries redundantly (each extra attempt is a harmless
                    // no-op, per the class remarks, but still wasted work).
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
                        "Recovery Verification Worker could not list active namespaces: {Error}",
                        namespacesResult.Error.Message);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in Recovery Verification Worker sweep cycle");
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

        _logger.LogInformation("Recovery Verification Worker stopped");
    }

    /// <summary>Sweeps one owner's due entries. Internal (rather than private) so tests can
    /// drive a single sweep cycle directly instead of waiting on <see cref="ExecuteAsync"/>'s
    /// timer loop.</summary>
    internal async Task SweepOwnerAsync(IServiceProvider services, string ownerId, CancellationToken cancellationToken)
    {
        var recoveryLedger = services.GetRequiredService<IRecoveryLedger>();
        var namespaceRepo = services.GetRequiredService<INamespaceRepository>();
        var router = services.GetRequiredService<CloudProviderRouter>();

        var nonTerminal = await recoveryLedger.GetAgeingAsync(ownerId, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var due = nonTerminal.Where(e =>
            e.State == RecoveryEntryState.Observing
            && e.ObservationWindowEndsAt is { } endsAt
            && endsAt <= now);

        var actor = ActorIdentityResolver.ResolveSystemActor("RecoveryVerificationWorker");

        foreach (var entry in due)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (outcome, reason) = await DetermineCoverageAsync(entry, namespaceRepo, router, cancellationToken);

            var result = await recoveryLedger.RecordObservationAsync(new RecordObservationRequest
            {
                EntryId = entry.Id,
                OwnerId = ownerId,
                Actor = actor,
                Outcome = outcome,
                DetailJson = reason is null ? null : JsonSerializer.Serialize(new { reason }),
            }, cancellationToken);

            if (result.IsFailure)
            {
                // Most commonly a lost race against DlqMonitorService's scan-time recurrence hook
                // (the entry already closed as Returned) or a concurrent sweep — not an error.
                _logger.LogDebug(
                    "Skipped closing recovery ledger entry {EntryId}: {Error}", entry.Id, result.Error.Message);
            }
            else
            {
                _logger.LogInformation(
                    "Recovery ledger entry {EntryId} closed as {Outcome}{Reason}",
                    entry.Id, outcome, reason is null ? string.Empty : $" ({reason})");
            }
        }
    }

    /// <summary>
    /// Decides whether an entry's observation window closed with proven, continuous, uncapped
    /// coverage. See the class remarks for why this is a static per-provider fact rather than a
    /// dynamic per-cycle log.
    /// </summary>
    internal static async Task<(RecoveryObservationOutcome Outcome, string? Reason)> DetermineCoverageAsync(
        RecoveryLedgerEntry entry,
        INamespaceRepository namespaceRepo,
        CloudProviderRouter router,
        CancellationToken cancellationToken)
    {
        if (entry.NamespaceId is not { } namespaceId)
        {
            return (RecoveryObservationOutcome.ObservationUnavailable, "NAMESPACE_UNKNOWN");
        }

        var nsResult = await namespaceRepo.GetByIdAsync(namespaceId, cancellationToken);
        if (nsResult.IsFailure)
        {
            return (RecoveryObservationOutcome.ObservationUnavailable, "NAMESPACE_DEREGISTERED");
        }

        var ns = nsResult.Value;
        if (!router.IsRegistered(ns.Provider))
        {
            return (RecoveryObservationOutcome.ObservationUnavailable,
                $"{ns.Provider.ToString().ToUpperInvariant()}_PROVIDER_NOT_REGISTERED");
        }

        var provider = router.Resolve(ns.Provider);
        return provider.Capabilities.CanProveDlqAbsence
            ? (RecoveryObservationOutcome.NoRecurrenceObserved, null)
            : (RecoveryObservationOutcome.ObservationUnavailable,
                $"{ns.Provider.ToString().ToUpperInvariant()}_NO_ABSENCE_PROOF");
    }
}
