namespace ServiceHub.Core.DTOs.Responses;

/// <summary>Response shape for a <c>GovernanceGrant</c> row.</summary>
public sealed record GovernanceGrantResponse(
    Guid Id,
    string GranteeIdentity,
    string GranteeKind,
    string Role,
    Guid? NamespaceId,
    string? PillarKind,
    DateTimeOffset GrantedAt,
    string GrantedByIdentity,
    DateTimeOffset? RevokedAt,
    string? RevokedByIdentity);
