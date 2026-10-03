namespace ServiceHub.Core.Models;

/// <summary>Result of recomputing and comparing one owner's hash chain. Tamper-EVIDENT, not
/// tamper-PROOF — see <c>IRecoveryLedger.VerifyChainAsync</c>.</summary>
public sealed class ChainVerificationResult
{
    /// <summary>The owner whose chain was verified.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Whether the chain is intact.</summary>
    public required bool IsValid { get; init; }

    /// <summary>The number of events examined.</summary>
    public required long EventsChecked { get; init; }

    /// <summary>The first <see cref="Entities.RecoveryEvent.Seq"/> at which the chain diverges
    /// from what recomputation expects. Null when <see cref="IsValid"/> is true.</summary>
    public long? FirstDivergentSeq { get; init; }

    /// <summary>Human-readable explanation of the divergence (modified event, incorrect
    /// PrevHash/EntryHash, or a sequence gap). Null when <see cref="IsValid"/> is true.</summary>
    public string? Reason { get; init; }
}
