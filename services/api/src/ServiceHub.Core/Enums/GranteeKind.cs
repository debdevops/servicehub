namespace ServiceHub.Core.Enums;

/// <summary>
/// What kind of identity a <see cref="ServiceHub.Core.Entities.GovernanceGrant"/> was granted to —
/// mirrors the same two-way split <c>RecoveryActorKind</c> draws between a human and a credential,
/// narrowed to the two kinds a grant's <c>GranteeIdentity</c> can actually be.
/// </summary>
public enum GranteeKind
{
    /// <summary>A human operator, identified by their resolved claims/SSO identity.</summary>
    User = 0,

    /// <summary>A scoped API key, identified by its configured name.</summary>
    ApiKey = 1,
}
