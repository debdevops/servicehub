using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>Request to propose a new <see cref="PlaybookEntry"/>.</summary>
public sealed record ProposePlaybookEntryRequest
{
    public required string OwnerId { get; init; }
    public required PillarKind PillarKind { get; init; }
    public required string ProposalKind { get; init; }
    public required string EvidenceRefJson { get; init; }
    public required string ProposalJson { get; init; }
    public required PlaybookActor Proposer { get; init; }
    public string? SignatureHashSnapshot { get; init; }
    public Guid? NamespaceId { get; init; }
    public string? NamespaceNameSnapshot { get; init; }
    public CloudProviderType? ProviderSnapshot { get; init; }
    public EnvironmentType? EnvironmentSnapshot { get; init; }
    public Guid? RelatedRecoveryOperationId { get; init; }
    public required TimeSpan ExpiresAfter { get; init; }
}

/// <summary>
/// The Playbook Ledger (M4 of the persistence wave) — the audit substrate for reasoning, never for
/// execution. Nothing written here ever authorizes a replay or purge; approving a proposal here
/// means "a human agrees this is sound," never itself calling <see cref="IRecoveryLedger"/>. See
/// <see cref="Entities.PlaybookEntry"/> for the full design rationale.
/// </summary>
public interface IPlaybookLedger
{
    /// <summary>Creates a new entry in the <see cref="PlaybookEntryState.Proposed"/> state and
    /// writes its first (<see cref="PlaybookEventType.Proposed"/>) event.</summary>
    Task<Result<PlaybookEntry>> ProposeAsync(ProposePlaybookEntryRequest request, CancellationToken cancellationToken = default);

    /// <summary>Marks an entry <see cref="PlaybookEntryState.UnderReview"/> — a UX nicety, valid
    /// only from <see cref="PlaybookEntryState.Proposed"/>.</summary>
    Task<Result<PlaybookEntry>> MarkUnderReviewAsync(Guid entryId, string ownerId, PlaybookActor actor, CancellationToken cancellationToken = default);

    /// <summary>Records an edit to the proposal's parameters before acceptance — valid only from
    /// <see cref="PlaybookEntryState.Proposed"/>/<see cref="PlaybookEntryState.UnderReview"/>. The
    /// original <see cref="PlaybookEntry.ProposalJson"/> is never overwritten; the new content is
    /// recorded on the event, so what was originally proposed stays reconstructable.</summary>
    Task<Result<PlaybookEntry>> ReviseAsync(Guid entryId, string ownerId, PlaybookActor actor, string revisedProposalJson, CancellationToken cancellationToken = default);

    /// <summary>Records a human's terminal decision — valid only from
    /// <see cref="PlaybookEntryState.Proposed"/>/<see cref="PlaybookEntryState.UnderReview"/>/
    /// <see cref="PlaybookEntryState.Edited"/>. <paramref name="reason"/> is required when
    /// <paramref name="disposition"/> is <see cref="PlaybookDisposition.Rejected"/>.</summary>
    Task<Result<PlaybookEntry>> DispositionAsync(Guid entryId, string ownerId, PlaybookActor actor, PlaybookDisposition disposition, string? reason, CancellationToken cancellationToken = default);

    /// <summary>Expires an entry that reached <see cref="PlaybookEntry.ExpiresAt"/> without a human
    /// decision — idempotent no-op if already terminal. Intended for a background sweep, so the
    /// actor is always resolved as <see cref="PlaybookActorKind.System"/> internally.</summary>
    Task<Result<PlaybookEntry>> ExpireAsync(Guid entryId, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Marks an entry superseded by a later proposal for the same subject — valid only
    /// from <see cref="PlaybookEntryState.Proposed"/>/<see cref="PlaybookEntryState.UnderReview"/>.</summary>
    Task<Result<PlaybookEntry>> SupersedeAsync(Guid entryId, string ownerId, PlaybookActor actor, Guid supersededByEntryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Turns off a standing, previously-<see cref="PlaybookEntryState.Approved"/> construct (e.g.
    /// a promoted P5 <c>PreventionRule</c> — <c>PREVENTION-RULE-DESIGN-2026-08-29.md</c> §9) —
    /// valid only from <see cref="PlaybookEntryState.Approved"/>, and only when the entry's
    /// <see cref="PlaybookEntry.ProposalKind"/> is on the ledger's own small, explicit revocable
    /// allow-list. A one-time decision (e.g. an approved <c>ReplayPlan</c> or
    /// <c>CorrelationHypothesis</c>) is never revocable through this method —
    /// <see cref="PlaybookEntryState.Approved"/> means "a human agreed this was sound," a
    /// permanent historical fact, for every
    /// <c>ProposalKind</c> not on that allow-list. <paramref name="reason"/> is always required,
    /// mirroring <see cref="DispositionAsync"/>'s requirement for <see cref="PlaybookDisposition.Rejected"/>.
    /// </summary>
    Task<Result<PlaybookEntry>> RevokeAsync(Guid entryId, string ownerId, PlaybookActor actor, string reason, CancellationToken cancellationToken = default);

    /// <summary>Queries entries for an owner, optionally narrowed by pillar, namespace, and/or state.</summary>
    Task<Result<IReadOnlyList<PlaybookEntry>>> QueryEntriesAsync(
        string ownerId, PillarKind? pillarKind = null, Guid? namespaceId = null, PlaybookEntryState? state = null, CancellationToken cancellationToken = default);

    /// <summary>Gets one entry by ID, scoped to its owner. Returns null if it doesn't exist or
    /// belongs to a different owner — mirrors <c>IRecoveryLedger.GetOperationAsync</c>.</summary>
    Task<PlaybookEntry?> GetEntryAsync(Guid entryId, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Gets an owner's non-terminal entries whose <see cref="PlaybookEntry.ExpiresAt"/> is
    /// at or before <paramref name="asOf"/>, oldest expiry first — the query a background expiry
    /// sweep drives <see cref="ExpireAsync"/> from. Unlike the Recovery Evidence Ledger's ageing
    /// sweep, no flag-then-expire two-pass is needed: <see cref="PlaybookEntry.ExpiresAt"/> is
    /// already fixed at proposal time, so a single pass is sufficient.</summary>
    Task<Result<IReadOnlyList<PlaybookEntry>>> GetDueForExpiryAsync(
        string ownerId, DateTimeOffset asOf, int limit = 1000, CancellationToken cancellationToken = default);

    /// <summary>Every event for one entry, ordered by <see cref="PlaybookEvent.Seq"/> ascending.</summary>
    Task<Result<IReadOnlyList<PlaybookEvent>>> GetEventsForEntryAsync(Guid entryId, string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Recomputes and verifies one owner's entire Playbook hash chain.</summary>
    Task<Models.ChainVerificationResult> VerifyChainAsync(string ownerId, CancellationToken cancellationToken = default);
}
