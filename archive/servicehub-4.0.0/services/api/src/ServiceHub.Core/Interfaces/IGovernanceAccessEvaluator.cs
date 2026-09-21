using ServiceHub.Core.Enums;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// The Governance/RBAC enforcement layer the persistence design's §14 deliberately left for a
/// future roadmap item (item 10) to build on top of the M3 <see cref="IGovernanceGrantService"/>
/// schema. Answers "does this identity's current set of active grants cover the role a specific
/// action requires," never mutates a grant itself.
/// <para>
/// This is a permission check layered <b>in addition to</b> the existing flat API-key scope model
/// (<c>ScopeAuthorizationFilter</c>) and, for Recover-pillar replay/purge, <b>alongside</b> — never
/// instead of — the Recovery Eligibility Gate's six ordered predicates. It never becomes a second
/// execution path for any action; it only decides whether the caller may reach the existing one.
/// </para>
/// </summary>
public interface IGovernanceAccessEvaluator
{
    /// <summary>
    /// Returns success when <paramref name="granteeIdentity"/> (or the owner-wide default grant —
    /// see the implementation's remarks on <c>GovernanceGrantSeeder</c>'s grandfathering
    /// convention) holds an active grant at role <paramref name="requiredRole"/> or higher, scoped
    /// to cover <paramref name="namespaceId"/>/<paramref name="pillarKind"/> (a null grant scope
    /// narrows to "all"). Returns success unconditionally when <paramref name="ownerId"/> has zero
    /// Governance grants at all — Governance has not been activated for that tenant yet, so
    /// behaviour is unchanged from before M3 shipped, matching the "additive-permissive, never
    /// restrictive" philosophy already documented on <c>GovernanceGrant</c>'s EF configuration.
    /// </summary>
    Task<Result> EvaluateAsync(
        string ownerId,
        string granteeIdentity,
        GovernanceRole requiredRole,
        Guid? namespaceId,
        PillarKind? pillarKind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the highest <see cref="GovernanceRole"/> currently applicable to
    /// <paramref name="granteeIdentity"/> for the given scope — for UI/"what can I do" purposes
    /// (see <c>MeController</c>), not for gating an action (use <see cref="EvaluateAsync"/> there).
    /// Returns <see cref="GovernanceRole.Admin"/> when <paramref name="ownerId"/> has zero grants
    /// at all (Governance not yet activated — access is unrestricted, the Admin-equivalent state),
    /// or <see langword="null"/> when Governance is active for this owner but no active grant
    /// covers this identity at this scope.
    /// </summary>
    Task<GovernanceRole?> GetEffectiveRoleAsync(
        string ownerId,
        string granteeIdentity,
        Guid? namespaceId,
        PillarKind? pillarKind,
        CancellationToken cancellationToken = default);
}
