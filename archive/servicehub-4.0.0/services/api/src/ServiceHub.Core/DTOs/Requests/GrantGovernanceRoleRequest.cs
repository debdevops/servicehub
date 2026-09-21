using ServiceHub.Core.Enums;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Request body for <c>POST /api/v1/governance/grants</c>. Deliberately excludes
/// <c>GrantedByIdentity</c> — the controller resolves that server-side, never from the request
/// body, matching every other actor-identity field in this codebase.</summary>
public sealed record GrantGovernanceRoleRequest(
    string GranteeIdentity,
    GranteeKind GranteeKind,
    GovernanceRole Role,
    Guid? NamespaceId,
    PillarKind? PillarKind);
