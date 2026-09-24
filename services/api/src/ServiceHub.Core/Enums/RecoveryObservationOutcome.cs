namespace ServiceHub.Core.Enums;

/// <summary>
/// What a DLQ scan during the post-replay observation window found, or could not determine.
/// Passed to <c>IRecoveryLedger.RecordObservationAsync</c>.
/// </summary>
public enum RecoveryObservationOutcome
{
    /// <summary>The replayed message (or, heuristically, one matching its body hash) reappeared
    /// in the DLQ within the window.</summary>
    RecurrenceObserved = 0,

    /// <summary>The window closed with continuous, uncapped scan coverage and no recurrence.</summary>
    NoRecurrenceObserved = 1,

    /// <summary>The window closed without adequate scan coverage to prove absence.</summary>
    ObservationUnavailable = 2
}
