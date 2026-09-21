namespace ServiceHub.Core.Enums;

/// <summary>
/// What the provider call underlying a <see cref="Entities.RecoveryLedgerEntry"/> actually
/// returned. Passed to <c>IRecoveryLedger.RecordExecutionAsync</c>.
/// </summary>
public enum RecoveryExecutionOutcome
{
    /// <summary>The provider accepted the call.</summary>
    Accepted = 0,

    /// <summary>The provider rejected the call.</summary>
    Rejected = 1,

    /// <summary>The process died mid-call; whether the provider accepted it is genuinely unknown.</summary>
    Unknown = 2
}
