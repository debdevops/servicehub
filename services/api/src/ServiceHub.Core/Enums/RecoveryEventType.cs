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

    // 13 (EmergencyStopActivated), 14 (EmergencyStopCleared), 15 (OutcomeFlagged), and 16
    // (RecurrenceCapObserved) are reserved by the roadmap (§9.4.1, §9.4.2, §8.10) — not yet
    // implemented; later Phase D tasks.

    /// <summary>An <see cref="Entities.AutonomyGrant"/> was promoted to a higher
    /// <see cref="AutonomyLevel"/> (roadmap §9.4.3).</summary>
    AutonomyGrantPromoted = 17,

    /// <summary>An <see cref="Entities.AutonomyGrant"/> was demoted to a lower
    /// <see cref="AutonomyLevel"/> (roadmap §9.4.3).</summary>
    AutonomyGrantDemoted = 18
}
