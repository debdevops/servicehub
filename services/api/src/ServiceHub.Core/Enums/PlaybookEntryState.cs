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

    /// <summary>An operator explicitly turned off a standing, re-evaluated construct (e.g. a
    /// promoted <c>PreventionRule</c>) that was previously <see cref="Approved"/>. Reachable only
    /// from <see cref="Approved"/>, and only for a <c>ProposalKind</c> on the ledger's own
    /// revocable allow-list — see <c>PlaybookLedgerService.RevocableProposalKinds</c>. Unlike
    /// <see cref="Rejected"/>/<see cref="Expired"/>/<see cref="Superseded"/>, this never closes out
    /// a one-time decision (those stay permanently un-revocable) — it only ever stops a standing
    /// condition from being re-evaluated going forward (roadmap P5, <c>PREVENTION-RULE-DESIGN-2026-08-29.md</c> §9).</summary>
    Revoked = 7,
}
