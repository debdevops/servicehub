namespace ServiceHub.Core.Enums;

/// <summary>One hash-chained fact about a <see cref="Entities.PlaybookEntry"/>'s lifecycle —
/// mirrors <c>RecoveryEventType</c>'s role for the Recovery Evidence Ledger.</summary>
public enum PlaybookEventType
{
    /// <summary>The entry was created.</summary>
    Proposed = 0,

    /// <summary>A human opened it for review.</summary>
    UnderReview = 1,

    /// <summary>A human changed the proposal's parameters before accepting.</summary>
    Revised = 2,

    /// <summary>A human accepted the proposal.</summary>
    Approved = 3,

    /// <summary>A human declined the proposal.</summary>
    Rejected = 4,

    /// <summary>The entry expired without a human decision.</summary>
    Expired = 5,

    /// <summary>A later proposal for the same subject made this one moot.</summary>
    Superseded = 6,
}
