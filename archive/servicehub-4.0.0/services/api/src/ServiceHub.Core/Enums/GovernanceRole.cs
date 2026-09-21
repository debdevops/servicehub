namespace ServiceHub.Core.Enums;

/// <summary>
/// The starting Governance/RBAC role set (persistence design §14) — a small, fixed enum,
/// structural like <see cref="PillarKind"/>, not expected to grow often: roles are rare and
/// stable, unlike <c>ProposalKind</c> strings, so the compile-time safety a real enum gives every
/// authorization check site is worth the migration cost of a rare future addition.
/// </summary>
public enum GovernanceRole
{
    /// <summary>Read-only access.</summary>
    Viewer = 0,

    /// <summary>Can create/enable rules and execute approvals within existing autonomy limits.</summary>
    Operator = 1,

    /// <summary>Can act on the L3 approval queue specifically.</summary>
    Approver = 2,

    /// <summary>Can manage namespaces and Governance grants themselves, fleet-wide by default.</summary>
    Admin = 3,
}
