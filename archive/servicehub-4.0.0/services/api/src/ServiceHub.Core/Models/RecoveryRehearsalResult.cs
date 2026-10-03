using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// <see cref="Interfaces.IRecoveryRehearsalService.RehearseAsync"/>'s result: what
/// <see cref="Interfaces.IRecoveryEligibilityGate"/> would decide for a real, already-recorded
/// recovery ledger entry's identity, evaluated as of now under a caller-chosen
/// <see cref="RecoveryActorKind"/> — never a decision that executed anything (roadmap §7 W1.2).
/// </summary>
/// <param name="EntryId">The <see cref="Entities.RecoveryLedgerEntry"/> whose identity (namespace,
/// entity, body hash, signature hash, provider, environment) was rehearsed.</param>
/// <param name="ActorKindEvaluated">The <see cref="RecoveryActorKind"/> the gate was evaluated
/// under — independent of the entry's own originating actor, so an operator can ask "what would
/// this decide for Automation" even against an entry a human originally attempted.</param>
/// <param name="Decision">The gate's verdict and reason, unchanged from what a real attempt with
/// this identity would receive right now.</param>
/// <param name="EvaluatedAt">When the rehearsal ran.</param>
public sealed record RecoveryRehearsalResult(
    Guid EntryId,
    RecoveryActorKind ActorKindEvaluated,
    EligibilityDecision Decision,
    DateTimeOffset EvaluatedAt);
