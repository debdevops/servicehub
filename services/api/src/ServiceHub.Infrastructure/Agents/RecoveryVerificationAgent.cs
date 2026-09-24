using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Identity;
using ServiceHub.Infrastructure.Telemetry;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// Closes the watch window a replay opened (unit 2.8): the honest answer to "did it work?", including
/// "we cannot prove it".
/// </summary>
/// <remarks>
/// <para>
/// A recurrence <i>inside</i> the window is recorded the moment a scan sees it (see the dead-letter scan);
/// this agent handles only the window's end, for entries that never saw one. It is a sweep over durable
/// state, so a restart loses nothing, and a second sweep racing the first is harmless — the ledger refuses a
/// second transition out of <c>Observing</c>.
/// </para>
/// <para>
/// <b>The decision that matters (R4):</b> "did not come back" is only claimed when the namespace's cloud can
/// prove the queue stayed empty (<see cref="ProviderCapabilities.CanProveDlqAbsence"/>), and only when the
/// namespace is still registered — otherwise ServiceHub cannot have been watching and the outcome is
/// <c>Unverified</c>. That is <b>not a failure</b>: the replay may have worked perfectly; what is unproven is
/// the confirmation. The decision is copied unchanged from 4.0.0's worker and never reads a provider's name.
/// </para>
/// It <b>observes</b>: closing an entry is ServiceHub's own bookkeeping, so <c>Changed</c> stays 0.
/// </remarks>
public sealed class RecoveryVerificationAgent : IAgent
{
    private const int DefaultSweepIntervalSeconds = 60;
    private const int DefaultMaxBatch = 1000;

    private readonly IServiceScopeFactory _scopes;
    private readonly ILogger<RecoveryVerificationAgent> _logger;
    private readonly int _maxBatch;

    /// <summary>Creates the agent. Cadence comes from <c>RecoveryEvidence:VerificationSweepIntervalSeconds</c>.</summary>
    public RecoveryVerificationAgent(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<RecoveryVerificationAgent> logger)
    {
        _scopes = scopes ?? throw new ArgumentNullException(nameof(scopes));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(configuration);

        var interval = TimeSpan.FromSeconds(Math.Clamp(
            configuration.GetValue("RecoveryEvidence:VerificationSweepIntervalSeconds", DefaultSweepIntervalSeconds), 10, 3600));
        _maxBatch = Math.Clamp(configuration.GetValue("RecoveryEvidence:MaxAgeingBatchSize", DefaultMaxBatch), 1, 100_000);

        Descriptor = new AgentDescriptor(
            Id: "recovery-verification",
            Name: "Replay Verifier",
            Purpose: "Watches each replayed message for the length of its watch window, and says whether it came back, stayed fixed, or cannot be proven.",
            Kind: AgentKind.Watch,
            Authority: AgentAuthority.Observes,
            Cadence: interval,
            Notes: "It only says a replay stayed fixed where the cloud can prove the dead-letter queue stayed empty. Elsewhere the result reads 'verification required'.");
    }

    /// <inheritdoc />
    public AgentDescriptor Descriptor { get; }

    /// <inheritdoc />
    public async Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
    {
        using var scope = _scopes.CreateScope();
        var namespaces = await scope.ServiceProvider.GetRequiredService<INamespaceRepository>().GetActiveAsync(ct).ConfigureAwait(false);
        if (namespaces.IsFailure)
        {
            throw new InvalidOperationException("The list of connected clouds could not be read.");
        }

        var closed = new Dictionary<RecoveryObservationOutcome, int>();
        var examined = 0;
        foreach (var ownerId in namespaces.Value.Select(n => n.OwnerId).Distinct(StringComparer.Ordinal))
        {
            examined += await SweepOwnerAsync(scope.ServiceProvider, ownerId, closed, ct).ConfigureAwait(false);
        }

        if (examined == 0)
        {
            return AgentCycleResult.Idle("no replay is waiting for its watch window to end");
        }

        var summary = string.Join(", ", closed.Select(kv => $"{kv.Value} {Describe(kv.Key)}"));
        return new AgentCycleResult(examined, 0, string.IsNullOrEmpty(summary) ? $"{examined} window(s) due, none closed" : summary);
    }

    private static string Describe(RecoveryObservationOutcome outcome) => outcome switch
    {
        RecoveryObservationOutcome.NoRecurrenceObserved => "verified (did not come back)",
        RecoveryObservationOutcome.ObservationUnavailable => "cannot be proven",
        _ => "came back",
    };

    /// <summary>Closes one owner's due entries. Returns how many were due.</summary>
    internal async Task<int> SweepOwnerAsync(
        IServiceProvider services, string ownerId, Dictionary<RecoveryObservationOutcome, int> closed, CancellationToken ct)
    {
        var ledger = services.GetRequiredService<IRecoveryLedger>();
        var namespaceRepo = services.GetRequiredService<INamespaceRepository>();
        var router = services.GetRequiredService<ICloudProviderRouter>();
        var metrics = services.GetService<ServiceHubMetrics>();

        var now = DateTimeOffset.UtcNow;
        var due = (await ledger.GetAgeingAsync(ownerId, _maxBatch, ct).ConfigureAwait(false))
            .Where(e => e.State == RecoveryEntryState.Observing && e.ObservationWindowEndsAt is { } end && end <= now)
            .ToList();

        var actor = ActorIdentityResolver.ResolveSystemActor("RecoveryVerificationAgent");
        foreach (var entry in due)
        {
            ct.ThrowIfCancellationRequested();
            var (outcome, reason) = await DetermineCoverageAsync(entry, namespaceRepo, router, ct).ConfigureAwait(false);

            var result = await ledger.RecordObservationAsync(new RecordObservationRequest
            {
                EntryId = entry.Id, OwnerId = ownerId, Actor = actor, Outcome = outcome,
                DetailJson = reason is null ? null : JsonSerializer.Serialize(new { reason }),
            }, ct).ConfigureAwait(false);

            if (result.IsFailure)
            {
                // Usually a lost race with a scan that already recorded a return — not an error.
                _logger.LogDebug("Skipped closing ledger entry {EntryId}: {Error}", entry.Id, result.Error.Message);
                continue;
            }

            closed[outcome] = closed.GetValueOrDefault(outcome) + 1;
            if (services.GetService<IPlatformEventBus>() is { } bus)
            {
                // An outcome changed: a screen showing "watching" should look again.
                await bus.PublishAsync(new PlatformEvent
                {
                    Source = Descriptor.Id, Category = EventCategories.Replay, EventType = EventTypes.ReplayCompleted,
                    NamespaceId = entry.NamespaceId, Actor = ownerId,
                }, ct).ConfigureAwait(false);
            }

            metrics?.RecordVerificationOutcome(outcome.ToString(), reason);
            _logger.LogInformation("Ledger entry {EntryId} closed as {Outcome}{Reason}", entry.Id, outcome, reason is null ? "" : $" ({reason})");
        }

        return due.Count;
    }

    /// <summary>
    /// Whether the window closed with proven coverage. Copied from 4.0.0 unchanged: a static, conservative,
    /// per-namespace capability — never the provider's name — and a missing namespace means nobody was watching.
    /// </summary>
    internal static async Task<(RecoveryObservationOutcome Outcome, string? Reason)> DetermineCoverageAsync(
        RecoveryLedgerEntry entry, INamespaceRepository namespaceRepo, ICloudProviderRouter router, CancellationToken ct)
    {
        if (entry.NamespaceId is not { } namespaceId)
        {
            return (RecoveryObservationOutcome.ObservationUnavailable, "NAMESPACE_UNKNOWN");
        }

        var nsResult = await namespaceRepo.GetByIdAsync(namespaceId, ct).ConfigureAwait(false);
        if (nsResult.IsFailure)
        {
            return (RecoveryObservationOutcome.ObservationUnavailable, "NAMESPACE_DEREGISTERED");
        }

        var ns = nsResult.Value;
        if (!router.IsRegistered(ns.Provider))
        {
            return (RecoveryObservationOutcome.ObservationUnavailable, $"{ns.Provider.ToString().ToUpperInvariant()}_PROVIDER_NOT_REGISTERED");
        }

        return router.Resolve(ns.Provider).Capabilities.CanProveDlqAbsence
            ? (RecoveryObservationOutcome.NoRecurrenceObserved, null)
            : (RecoveryObservationOutcome.ObservationUnavailable, $"{ns.Provider.ToString().ToUpperInvariant()}_NO_ABSENCE_PROOF");
    }
}
