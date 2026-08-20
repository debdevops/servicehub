namespace ServiceHub.Core.Enums;

/// <summary>
/// Why a <see cref="DlqMessageStatus.Resolved"/> message left the DLQ. Distinguishes what
/// ServiceHub actually observed from what it merely infers, so "Resolved" never silently
/// implies "ServiceHub replayed this" the way the old <see cref="DlqMessageStatus.Replayed"/>
/// fabrication did.
/// </summary>
public enum DlqResolutionCause
{
    /// <summary>ServiceHub's own replay path sent this message. Not assignable until the
    /// Recovery Evidence Ledger (a later phase) can prove it.</summary>
    ReplayedByServiceHub = 0,

    /// <summary>ServiceHub's own purge path deleted this message. Not assignable until the
    /// Recovery Evidence Ledger (a later phase) can prove it.</summary>
    PurgedByServiceHub = 1,

    /// <summary>The message left the DLQ by some means ServiceHub did not perform or cannot
    /// attribute — external drain, TTL expiry, another tool, or a genuine ServiceHub action it
    /// has no evidence for yet.</summary>
    VanishedExternally = 2,

    /// <summary>An operator manually triaged the message to Resolved without a ServiceHub
    /// provider call backing that assertion.</summary>
    DeclaredByOperator = 3,

    /// <summary>Recorded before this field existed; the cause was never determined.</summary>
    Unknown = 4
}
