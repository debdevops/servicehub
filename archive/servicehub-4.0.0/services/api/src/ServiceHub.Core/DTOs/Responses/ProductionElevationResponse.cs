namespace ServiceHub.Core.DTOs.Responses;

/// <summary>Response shape for a <c>ProductionElevation</c> row. <see cref="IsLive"/> is the
/// single fact the Recovery Eligibility Gate's predicate 2 and every front-door Prod guard
/// evaluate: approved, unrevoked, unexpired.</summary>
public sealed record ProductionElevationResponse(
    Guid Id,
    Guid NamespaceId,
    string? NamespaceNameSnapshot,
    string Reason,
    string RequestedByIdentity,
    DateTimeOffset RequestedAt,
    TimeSpan RequestedDuration,
    string? ApprovedByIdentity,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokedByIdentity,
    bool IsLive);
