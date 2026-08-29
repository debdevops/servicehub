using ServiceHub.Core.Enums;

namespace ServiceHub.Api.Authorization;

/// <summary>
/// Marks a controller or action as requiring a minimum <see cref="GovernanceRole"/>, enforced by
/// <see cref="ServiceHub.Api.Filters.GovernanceAuthorizationFilter"/> against a <c>namespaceId</c>
/// read from the route or query string (same convention as
/// <see cref="ServiceHub.Api.Filters.RequireNamespaceOwnershipAttribute"/>), when present.
/// <para>
/// Layered on top of, never instead of, <see cref="RequireScopeAttribute"/>: a flat API-key scope
/// answers "can any caller holding this credential ever perform this class of action"; this
/// answers "does the specific identity behind that credential hold the Governance role this owner
/// has configured for it." Unlike <see cref="RequireScopeAttribute"/>'s enforcement
/// (<c>ScopeAuthorizationFilter</c>), this check is <b>not</b> bypassed for SPA/EasyAuth callers —
/// those are exactly the identities Governance/RBAC exists to differentiate (persistence design
/// §14, master roadmap §6 item 3).
/// </para>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
public sealed class RequireGovernanceRoleAttribute : Attribute
{
    /// <summary>The minimum role required.</summary>
    public GovernanceRole Role { get; }

    /// <summary>The pillar this check is scoped to, or <see langword="null"/> for any pillar.</summary>
    public PillarKind? PillarKind { get; }

    /// <summary>Requires <paramref name="role"/>, for any pillar.</summary>
    public RequireGovernanceRoleAttribute(GovernanceRole role)
    {
        Role = role;
        PillarKind = null;
    }

    /// <summary>Requires <paramref name="role"/>, scoped to <paramref name="pillarKind"/> only.</summary>
    public RequireGovernanceRoleAttribute(GovernanceRole role, PillarKind pillarKind)
    {
        Role = role;
        PillarKind = pillarKind;
    }
}
