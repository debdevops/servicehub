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

    /// <summary>The operation activated or cleared an owner-scoped emergency stop — a kill
    /// switch on new <c>Automation</c>/<c>System</c> autonomous execution (roadmap §9.4.2,
    /// §15.2). Never represents a replay/purge and never touches an
    /// <see cref="Entities.AutonomyGrant"/>.</summary>
    EmergencyControl = 2,

    /// <summary>The operation was an <see cref="Entities.AutonomyGrant"/> promotion or demotion
    /// (roadmap §9.4.3).</summary>
    AutonomyGrantChange = 3,

    /// <summary>The operation automatically disabled an <see cref="Entities.AutoReplayRule"/> —
    /// the success-rate circuit breaker. Never represents a replay/purge and never touches an
    /// <see cref="Entities.AutonomyGrant"/>.</summary>
    AutoReplayRuleControl = 4
}
