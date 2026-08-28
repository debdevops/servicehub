using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// One <see cref="RecoveryEventType.AutonomyGrantPromoted"/>/<see cref="RecoveryEventType.AutonomyGrantDemoted"/>
/// event, decoded from its <c>DetailJson</c> — the fleet-wide autonomy dashboard's "recent
/// activity" feed source (roadmap §11 item 5, §15 item 9). Read-only: producing this record never
/// writes anything and mirrors exactly what <see cref="Interfaces.IRecoveryLedger.RecordAutonomyGrantTransitionAsync"/>
/// already persisted, never a re-derivation of trust logic.
/// </summary>
public sealed record AutonomyTransitionRecord(
    string SignatureHash,
    RecoveryOperationKind ActionKind,
    AutonomyLevel PreviousLevel,
    AutonomyLevel NewLevel,
    string Reason,
    DateTimeOffset OccurredAtUtc);
