using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Rehearsal mode (roadmap §7 W1.2): runs <see cref="IRecoveryEligibilityGate"/> against one
/// existing <see cref="Entities.RecoveryLedgerEntry"/>'s recorded identity and reports what it
/// would decide, with no mutation path at all — architecturally incapable of reaching a broker,
/// not merely configured not to. This service depends on nothing that can execute a recovery
/// action (no <c>IAutoReplayExecutor</c>, no provider client, no ledger write method), so there is
/// no code path from here to a broker call regardless of the verdict returned. Serves three
/// audiences: a test that can exercise the accept path without a real provider, an operator who
/// wants to understand why an entry was declined or escalated, and a security reviewer who wants
/// to see the gate's reasoning without granting it anything.
/// </summary>
public interface IRecoveryRehearsalService
{
    /// <summary>
    /// Rehearses the gate against <paramref name="entryId"/>'s recorded namespace, entity, body
    /// hash, signature hash, provider, and environment — using the entry's parent
    /// <see cref="Entities.RecoveryOperation"/> for <c>ActionKind</c>/<c>Trigger</c> — evaluated
    /// under <paramref name="actorKind"/> against live ledger state (emergency stop, autonomy
    /// grant, recurrence lineage) as of now. <paramref name="actorKind"/> is independent of the
    /// entry's own originating actor: rehearsing a human-attempted entry as
    /// <see cref="RecoveryActorKind.Automation"/> is the whole point of predicate 5's reachability
    /// for a "would this ever auto-replay" question.
    /// </summary>
    /// <param name="entryId">The ledger entry to rehearse, scoped to <paramref name="ownerId"/>.</param>
    /// <param name="ownerId">Tenant-isolation filter — fails with a not-found error for an entry
    /// belonging to a different owner, same as every other per-entry read.</param>
    /// <param name="actorKind">Which actor kind to evaluate the gate as.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<RecoveryRehearsalResult>> RehearseAsync(
        Guid entryId,
        string ownerId,
        RecoveryActorKind actorKind,
        CancellationToken cancellationToken = default);
}
