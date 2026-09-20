namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Response DTO for a <see cref="Entities.PlaybookEntry"/> — one proposal's current, mutable
/// projection. <c>ProposalJson</c>/<c>EvidenceRefJson</c> are already redacted by
/// <c>IPlaybookLedger.ProposeAsync</c> before persisting, so no further filtering happens here.
/// </summary>
public sealed record PlaybookEntryResponse(
    Guid Id,
    string PillarKind,
    string ProposalKind,
    string EvidenceRefJson,
    string ProposalJson,
    DateTimeOffset ProposedAt,
    string ProposerIdentity,
    string ProposerKind,
    string? SignatureHashSnapshot,
    Guid? NamespaceId,
    string? NamespaceNameSnapshot,
    string? ProviderSnapshot,
    string? EnvironmentSnapshot,
    Guid? RelatedRecoveryOperationId,
    DateTimeOffset ExpiresAt,
    string State,
    string? Disposition,
    DateTimeOffset? ClosedAt);

/// <summary>
/// Response DTO for a <see cref="Entities.PlaybookEvent"/> — one append-only, hash-chained fact
/// on the Playbook Ledger's own, fully independent chain.
/// </summary>
public sealed record PlaybookEventResponse(
    Guid Id,
    long Seq,
    Guid EntryId,
    string EventType,
    DateTimeOffset OccurredAt,
    string ActorIdentity,
    string ActorKind,
    string? DetailJson,
    string PrevHash,
    string EntryHash,
    int SchemaVersion);

/// <summary>
/// Response DTO for <c>GET /api/v1/playbook/entries/{id}</c> — the entry's current projection plus
/// its full event chain, Seq-ordered, for the entry detail view.
/// </summary>
public sealed record PlaybookEntryDetailResponse(
    PlaybookEntryResponse Entry,
    IReadOnlyList<PlaybookEventResponse> Events);
