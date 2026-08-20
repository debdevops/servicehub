namespace ServiceHub.Core.Enums;

/// <summary>
/// What caused a <see cref="Entities.RecoveryOperation"/> to open — distinguishes an operator's
/// direct action from the various automated paths that can also move or destroy messages.
/// </summary>
public enum RecoveryTrigger
{
    /// <summary>An operator triggered a single-message replay or purge directly.</summary>
    Manual = 0,

    /// <summary>A bulk operation job (filter-driven replay/purge across many messages) triggered it.</summary>
    BulkJob = 1,

    /// <summary>A signature-scoped replay job triggered it.</summary>
    SignatureJob = 2,

    /// <summary>The Rules page's manual "Replay All" action triggered it.</summary>
    RuleReplayAll = 3,

    /// <summary>An enabled AutoReplay rule fired without operator involvement.</summary>
    AutoRule = 4,

    /// <summary>Startup reconciliation of a claim stranded by a prior process crash.</summary>
    StartupRecovery = 5,

    /// <summary>An administrator activated or cleared an owner-scoped emergency stop (roadmap
    /// §9.4.2, §15.2).</summary>
    EmergencyControl = 6,

    /// <summary>An <see cref="Entities.AutonomyGrant"/> promotion or demotion triggered it
    /// (roadmap §9.4.3).</summary>
    AutonomyEvaluation = 7,

    /// <summary>The success-rate circuit breaker automatically disabled an
    /// <see cref="Entities.AutoReplayRule"/> — never a grant change, never a replay/purge.</summary>
    AutoReplayCircuitBreaker = 8
}
