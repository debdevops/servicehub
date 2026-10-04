using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.DlqObserver;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>
/// How a replay's watch window closes on a cloud that cannot prove a fix by an ordinary peek, when that namespace has a
/// live whole view of its dead-letter queue (ADR-0018). Used by <see cref="RecoveryVerificationAgent"/> before its
/// older observer-log path.
/// </summary>
/// <remarks>
/// It never names a cloud (R4): the cloud is found by the namespace's provider, and reason codes are built from it.
/// Every unexpected failure closes as "cannot be proven" — never as "verified".
/// </remarks>
internal static class DeadLetterViewVerdict
{
    /// <summary>How long after its window ends a replay may stay open waiting for a view that can answer.</summary>
    internal static readonly TimeSpan GiveUpAfter = TimeSpan.FromHours(6);

    /// <summary>What to do with one due entry.</summary>
    /// <param name="Handled">False when there is no whole view for this entry and the caller should use its own path.</param>
    /// <param name="LeaveOpen">True when the entry must not be closed this sweep.</param>
    /// <param name="Outcome">The outcome to record, when it is to be closed.</param>
    /// <param name="Reason">Its reason code.</param>
    /// <param name="Confidence">Its confidence, for a return.</param>
    internal readonly record struct Decision(
        bool Handled, bool LeaveOpen = false, RecoveryObservationOutcome Outcome = default, string? Reason = null, VerificationConfidence? Confidence = null)
    {
        internal static readonly Decision NotMine = new(false);
        internal static readonly Decision Wait = new(true, LeaveOpen: true);
    }

    internal static async Task<Decision> DecideAsync(
        RecoveryLedgerEntry entry,
        INamespaceRepository namespaceRepo,
        ICloudProviderRouter router,
        IDlqObserverAttestationService? attestationService,
        IEnumerable<IDeadLetterReturnCheck>? checks,
        DeadLetterViewTracker? tracker,
        DateTimeOffset now,
        ILogger logger,
        CancellationToken ct)
    {
        if (attestationService is null || checks is null || entry.NamespaceId is not { } namespaceId)
        {
            return Decision.NotMine;
        }

        var found = await namespaceRepo.GetByIdAsync(namespaceId, ct).ConfigureAwait(false);
        if (found.IsFailure || !router.IsRegistered(found.Value.Provider) || router.Resolve(found.Value.Provider).Capabilities.CanProveDlqAbsence)
        {
            return Decision.NotMine; // gone, unknown, or a cloud that proves it by itself: the caller's own path decides
        }

        var ns = found.Value;
        var check = checks.FirstOrDefault(c => c.Provider == ns.Provider);
        if (check is null)
        {
            return Decision.NotMine;
        }

        var prefix = ns.Provider.ToString().ToUpperInvariant();
        DlqObserverAttestation? attestation;
        try
        {
            attestation = await attestationService.GetAsync(entry.OwnerId, namespaceId, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Dead-letter view attestation could not be read for namespace {NamespaceId}; failing closed", namespaceId);
            return Decision.NotMine;
        }

        if (attestation is null || !attestation.IsLiveAt(now))
        {
            return Decision.NotMine; // not switched on, or not confirmed lately: exactly as before this existed
        }

        DeadLetterReturnVerdict verdict;
        try
        {
            verdict = await check.CheckAsync(ns, attestation, entry, tracker?.LiveSince(entry.OwnerId, namespaceId), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Dead-letter view check failed for entry {EntryId}; failing closed", entry.Id);
            verdict = DeadLetterReturnVerdict.CannotTell("VIEW_CHECK_FAILED");
        }

        switch (verdict.Outcome)
        {
            case DeadLetterReturnOutcome.NotReturned:
                return new Decision(true, false, RecoveryObservationOutcome.NoRecurrenceObserved, $"{prefix}_VIEW_CONFIRMED_ABSENCE");
            case DeadLetterReturnOutcome.Returned:
                return new Decision(true, false, RecoveryObservationOutcome.RecurrenceObserved, $"{prefix}_VIEW_CONFIRMED_RETURN", VerificationConfidence.Exact);
            case DeadLetterReturnOutcome.TooEarly:
                return Decision.Wait;
            default:
                // A view that cannot answer now may be able to next time (a scan can be complete on the next sweep), so the
                // entry stays open for a while — and then closes honestly as "cannot be proven".
                var windowEnd = entry.ObservationWindowEndsAt ?? now;
                return now - windowEnd < GiveUpAfter
                    ? Decision.Wait
                    : new Decision(true, false, RecoveryObservationOutcome.ObservationUnavailable, $"{prefix}_{verdict.Reason ?? "VIEW_CANNOT_TELL"}");
        }
    }
}
