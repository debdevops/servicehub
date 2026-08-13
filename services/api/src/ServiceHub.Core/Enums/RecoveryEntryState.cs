namespace ServiceHub.Core.Enums;

/// <summary>
/// The lifecycle state of one <see cref="Entities.RecoveryLedgerEntry"/>. Transitions are
/// controlled exclusively by <see cref="Interfaces.IRecoveryLedger"/> — never assigned directly.
/// </summary>
public enum RecoveryEntryState
{
    /// <summary>Claim committed; the provider call is in flight.</summary>
    Executing = 0,

    /// <summary>Provider accepted a replay; the observation window is open.</summary>
    Observing = 1,

    /// <summary>Provider rejected the call. Terminal.</summary>
    ExecutionFailed = 2,

    /// <summary>The process died mid-call; the outcome is genuinely unknown. A re-attempt opens
    /// a new entry rather than transitioning this one further.</summary>
    ExecutionUnknown = 3,

    /// <summary>Accepted and no recurrence was observed for the full window with adequate
    /// coverage. Terminal. Means "did not return" — never "the business transaction completed."</summary>
    Recovered = 4,

    /// <summary>A recurrence was observed within the observation window. Terminal.</summary>
    Returned = 5,

    /// <summary>Purge accepted by the provider. Terminal, and truthful — a deliberate destruction.</summary>
    Discarded = 6,

    /// <summary>The observation window closed but observation was not possible or coverage was
    /// inadequate. Terminal.</summary>
    Unverified = 7,

    /// <summary>An operator declared this entry unrecoverable. Terminal. Requires a reason.</summary>
    WrittenOff = 8,

    /// <summary>Aged past the declared threshold and was surfaced on the ageing report first.
    /// Terminal. Reserved — nothing transitions an entry into this state yet; that is the
    /// ageing worker's responsibility, added in a later phase.</summary>
    Expired = 9
}
