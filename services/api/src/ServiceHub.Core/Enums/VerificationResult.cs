namespace ServiceHub.Core.Enums;

/// <summary>
/// The recurrence-verification outcome recorded on a <see cref="Entities.RecoveryLedgerEntry"/>.
/// Distinct from <see cref="RecoveryDisposition"/>: this describes specifically what the
/// observation window found, and is only meaningful for entries that opened one (replays, not
/// purges or write-offs).
/// </summary>
public enum VerificationResult
{
    /// <summary>No observation window applies to this entry (e.g. a purge).</summary>
    NotApplicable = 0,

    /// <summary>No recurrence observed for the full window with adequate coverage.</summary>
    Recovered = 1,

    /// <summary>A recurrence was observed within the window.</summary>
    Returned = 2,

    /// <summary>The window closed without adequate coverage to prove absence.</summary>
    Unverified = 3
}
