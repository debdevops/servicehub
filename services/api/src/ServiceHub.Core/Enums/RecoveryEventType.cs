namespace ServiceHub.Core.Enums;

/// <summary>
/// The kind of fact recorded by one append-only <see cref="Entities.RecoveryEvent"/> in the
/// hash-chained ledger.
/// </summary>
public enum RecoveryEventType
{
    /// <summary>A <see cref="Entities.RecoveryOperation"/> was opened.</summary>
    OperationOpened = 0,

    /// <summary>A <see cref="Entities.RecoveryLedgerEntry"/> was begun under an operation.</summary>
    EntryBegun = 1,

    /// <summary>The provider accepted the replay/purge call.</summary>
    ProviderAccepted = 2,

    /// <summary>The provider rejected the replay/purge call.</summary>
    ProviderRejected = 3,

    /// <summary>The process died mid-call; the outcome is genuinely unknown.</summary>
    ExecutionUnknown = 4,

    /// <summary>The post-replay observation window was opened.</summary>
    ObservationWindowOpened = 5,

    /// <summary>A recurrence of the replayed message was observed within the window.</summary>
    RecurrenceObserved = 6,

    /// <summary>The observation window closed with continuous, uncapped coverage and no recurrence.</summary>
    NoRecurrenceObserved = 7,

    /// <summary>The observation window closed without adequate coverage to prove absence.</summary>
    ObservationUnavailable = 8,

    /// <summary>An operator or the ledger set a terminal disposition on the entry.</summary>
    DispositionSet = 9,

    /// <summary>The entry was flagged by the ageing worker
    /// (<c>RecoveryAgeingWorker</c>/<see cref="Interfaces.IRecoveryLedger.FlagAgeingAsync"/>).
    /// <see cref="RecoveryEntryState.Expired"/> is reachable only through a transition whose
    /// preceding event is this one.</summary>
    AgeingFlagged = 10,

    /// <summary>A free-text operator annotation, appended without changing entry state.</summary>
    OperatorNote = 11,

    /// <summary>A deterministic eligibility check blocked a recovery attempt before any provider
    /// was contacted — e.g. the recurrence-lineage cap (roadmap §7.5, §9.3). Recorded instead of
    /// <c>AuditService</c>, which is a bounded, lossy channel unsuitable as forensic evidence.</summary>
    EligibilityDeclined = 12,

    /// <summary>An owner-scoped emergency stop was activated — the Eligibility Gate's predicate
    /// 0 (roadmap §9.4.2, §15.2) resolves every subsequent <c>Automation</c>/<c>System</c>
    /// request to <c>Escalate</c> until cleared. Never affects manual <c>User</c>/<c>ApiKey</c>
    /// recovery, never touches an <see cref="Entities.AutonomyGrant"/> row.</summary>
    EmergencyStopActivated = 13,

    /// <summary>An owner-scoped emergency stop was cleared — restores predicate 0's ability to
    /// resolve to <c>Allow</c> for the remaining predicates (roadmap §9.4.2, §15.2). Does not by
    /// itself un-demote any grant the independent, always-on demotion rules already caught.</summary>
    EmergencyStopCleared = 14,

    // 15 (OutcomeFlagged) is reserved by the roadmap (§8.10) — not yet implemented; a later
    // Phase D task.

    /// <summary>A recurrence-lineage cap (roadmap §7.5) was reached for this attempt's lineage,
    /// but the actor was <c>User</c>/<c>ApiKey</c> and predicate 3 did not auto-deny it (§29.11
    /// Option B) — recorded as observability context on the real entry the attempt produced.
    /// Never a state transition, never implying the entry itself is anomalous (roadmap
    /// §9.4.1).</summary>
    RecurrenceCapObserved = 16,

    /// <summary>An <see cref="Entities.AutonomyGrant"/> was promoted to a higher
    /// <see cref="AutonomyLevel"/> (roadmap §9.4.3).</summary>
    AutonomyGrantPromoted = 17,

    /// <summary>An <see cref="Entities.AutonomyGrant"/> was demoted to a lower
    /// <see cref="AutonomyLevel"/> (roadmap §9.4.3).</summary>
    AutonomyGrantDemoted = 18
}
