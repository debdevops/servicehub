namespace ServiceHub.Core.Enums;

/// <summary>
/// A <see cref="Entities.PlaybookEntry"/>'s lifecycle state — a derived, queryable projection over
/// its <see cref="Entities.PlaybookEvent"/> chain, never the source of truth itself (the same split
/// <c>RecoveryEntryState</c> already uses for the Recovery Evidence Ledger).
/// </summary>
public enum PlaybookEntryState
{
    /// <summary>The proposal exists, backed by evidence; nothing has actioned it yet.</summary>
    Proposed = 0,

    /// <summary>A human has opened it — a UX nicety, non-load-bearing.</summary>
    UnderReview = 1,

    /// <summary>A human accepted the proposal as-is.</summary>
    Approved = 2,

    /// <summary>A human changed the proposal's parameters before accepting.</summary>
    Edited = 3,

    /// <summary>A human declined it.</summary>
    Rejected = 4,

    /// <summary>Nobody actioned it before its expiry — terminal, not a failure.</summary>
    Expired = 5,

    /// <summary>A later proposal for the same subject made this one moot.</summary>
    Superseded = 6,
}
