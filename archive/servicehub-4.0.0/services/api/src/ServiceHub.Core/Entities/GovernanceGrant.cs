using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// One row per (grantee, namespace-scope, pillar-scope) access grant — the durable, per-owner,
/// per-namespace, per-pillar role model (persistence design §14, M3 of the persistence wave).
/// Deliberately <b>not</b> hash-chained, unlike the Recovery/Playbook Ledgers: a grant is current,
/// intentionally mutable configuration about who may act — like <see cref="AutoReplayRule.Enabled"/>
/// or <see cref="Namespace.IsActive"/> — not a claim about what happened. Soft-revoke only (never
/// deleted), so a grant's full history is always reconstructable; every grant/revoke additionally
/// writes an <see cref="AuditLog"/> entry (see <c>IGovernanceGrantService</c>), the right durability
/// tier for "an admin changed a permission."
/// <para>
/// M3 ships the durable record only — no authorization <i>enforcement</i> layer (middleware, policy
/// handlers, controller wiring) exists yet; that is a separate, future roadmap item this schema is
/// built to support.
/// </para>
/// </summary>
public sealed class GovernanceGrant
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner ID for multi-tenant isolation — a grant can never be queried or resolved
    /// across owners. Same format as <see cref="DlqMessage.OwnerId"/>.</summary>
    public required string OwnerId { get; init; }

    /// <summary>
    /// The identity being granted access — resolved server-side (claims identity name or API key
    /// name), never a caller-supplied string. Never re-used to grant one owner's identity access
    /// under a different owner's partition.
    /// </summary>
    public required string GranteeIdentity { get; init; }

    /// <summary>Whether <see cref="GranteeIdentity"/> is a human user or an API key.</summary>
    public required GranteeKind GranteeKind { get; init; }

    /// <summary>The role granted. Immutable for the lifetime of this row — changing a grantee's
    /// role means revoking this grant and creating a new one, so "who could do what, when" stays
    /// answerable from the revoke/grant history rather than an in-place role edit.</summary>
    public required GovernanceRole Role { get; init; }

    /// <summary>Namespace this grant is scoped to — soft reference, no FK (same convention as
    /// every other ledger-adjacent <c>NamespaceId</c>). Null means fleet-wide.</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>Pillar this grant is scoped to. Null means it applies to all four pillars.</summary>
    public PillarKind? PillarKind { get; init; }

    /// <summary>When this grant was created.</summary>
    public required DateTimeOffset GrantedAt { get; init; }

    /// <summary>The resolved actor identity that created this grant — never caller-supplied.</summary>
    public required string GrantedByIdentity { get; init; }

    /// <summary>When this grant was revoked, or null while still active. Set once, never cleared —
    /// a revoked grant stays revoked forever; granting the same scope again creates a new row.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>The resolved actor identity that revoked this grant — never caller-supplied. Null
    /// while <see cref="RevokedAt"/> is null.</summary>
    public string? RevokedByIdentity { get; set; }
}
