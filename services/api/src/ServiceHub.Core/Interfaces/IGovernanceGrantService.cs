using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Request to grant a <see cref="GovernanceRole"/> to an identity — <see cref="GrantedByIdentity"/>
/// must be resolved server-side by the caller (e.g. via an actor-resolution pattern like
/// <c>ActorIdentityResolver</c>), never a caller-supplied string from an untrusted request body.
/// </summary>
public sealed record GrantRoleRequest(
    string OwnerId,
    string GranteeIdentity,
    GranteeKind GranteeKind,
    GovernanceRole Role,
    Guid? NamespaceId,
    PillarKind? PillarKind,
    string GrantedByIdentity);

/// <summary>
/// Governance/RBAC grant management (M3 of the persistence wave) — the durable record a future
/// authorization enforcement layer (roadmap item 10) will read from. This service itself enforces
/// no authorization decisions; it only manages the grants themselves.
/// </summary>
public interface IGovernanceGrantService
{
    /// <summary>
    /// Creates a new grant, or fails with a conflict if an active grant already exists for the
    /// exact same (grantee, namespace scope, pillar scope) — including the fleet-wide/all-pillar
    /// (null, null) case the database's own filtered unique index cannot fully enforce due to SQL
    /// NULL semantics (see <c>DlqDbContext.ConfigureGovernanceGrant</c>). Also writes an
    /// <see cref="AuditLog"/> entry.
    /// </summary>
    Task<Result<GovernanceGrant>> GrantAsync(GrantRoleRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Soft-revokes a grant — sets <see cref="GovernanceGrant.RevokedAt"/>/<see cref="GovernanceGrant.RevokedByIdentity"/>,
    /// never deletes the row. Idempotent: revoking an already-revoked grant is a no-op success.
    /// Also writes an <see cref="AuditLog"/> entry.
    /// </summary>
    Task<Result> RevokeAsync(Guid grantId, string ownerId, string revokedByIdentity, CancellationToken cancellationToken = default);

    /// <summary>Every currently-active (non-revoked) grant for an owner.</summary>
    Task<Result<IReadOnlyList<GovernanceGrant>>> GetActiveGrantsAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>Every currently-active (non-revoked) grant for one grantee within an owner.</summary>
    Task<Result<IReadOnlyList<GovernanceGrant>>> GetGrantsForGranteeAsync(
        string ownerId, string granteeIdentity, CancellationToken cancellationToken = default);
}
