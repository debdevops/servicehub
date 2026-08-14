namespace ServiceHub.Core.Enums;

/// <summary>
/// The kind of provider mutation a <see cref="Entities.RecoveryOperation"/> represents.
/// </summary>
public enum RecoveryOperationKind
{
    /// <summary>The operation replayed one or more messages back to a live entity.</summary>
    Replay = 0,

    /// <summary>The operation permanently deleted one or more messages from the DLQ.</summary>
    Purge = 1,

    // 2 is reserved for RecoveryOperationKind.EmergencyControl (roadmap §9.4.2, §15.2) — not
    // yet implemented; a later Phase D task.

    /// <summary>The operation was an <see cref="Entities.AutonomyGrant"/> promotion or demotion
    /// (roadmap §9.4.3).</summary>
    AutonomyGrantChange = 3
}
