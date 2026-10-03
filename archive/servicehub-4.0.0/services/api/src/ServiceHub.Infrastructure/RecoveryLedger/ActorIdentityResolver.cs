using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// Resolves a <see cref="RecoveryActor"/> from pre-extracted primitives — never from
/// <c>HttpContext</c> directly. <c>ServiceHub.Infrastructure</c> has no dependency on ASP.NET
/// Core Http (by design — see CLAUDE.md's layering: HTTP concerns belong to
/// <c>ServiceHub.Api</c>), so callers in the Api layer extract the same values
/// <c>SecurityAuditLogger.ResolveUserIdentity</c> already reads from <c>HttpContext</c> and pass
/// them in here.
/// </summary>
public static class ActorIdentityResolver
{
    /// <summary>
    /// Prefix every API-key-authenticated actor identity carries. Governance grants for a
    /// <see cref="GranteeKind.ApiKey"/> grantee must be stored with this same prefix (see
    /// <c>GovernanceGrantService.GrantAsync</c>) or they silently never match the identity this
    /// resolver produces at request time — never applying instead of failing loudly.
    /// </summary>
    public const string ApiKeyIdentityPrefix = "ApiKey:";

    /// <summary>
    /// Resolves an HTTP-originated actor using the same precedence as
    /// <c>SecurityAuditLogger.ResolveUserIdentity</c>: API key name, then claims identity name,
    /// then owner ID, then "Unknown". Only a resolved API key name yields
    /// <see cref="RecoveryActorKind.ApiKey"/>; the claims and owner-ID fallbacks resolve to
    /// <see cref="RecoveryActorKind.User"/>, since at that point the caller could be an SPA
    /// session or another non-API-key identity and asserting ApiKey would be a guess.
    /// </summary>
    public static RecoveryActor ResolveHttpActor(
        string? apiKeyName,
        string? claimsIdentityName,
        string? ownerId,
        string? scopes = null)
    {
        if (!string.IsNullOrEmpty(apiKeyName))
        {
            return new RecoveryActor($"{ApiKeyIdentityPrefix}{apiKeyName}", RecoveryActorKind.ApiKey, scopes);
        }

        if (!string.IsNullOrEmpty(claimsIdentityName))
        {
            return new RecoveryActor(claimsIdentityName, RecoveryActorKind.User, scopes);
        }

        if (!string.IsNullOrEmpty(ownerId))
        {
            return new RecoveryActor(ownerId, RecoveryActorKind.User, scopes);
        }

        return new RecoveryActor("Unknown", RecoveryActorKind.User, scopes);
    }

    /// <summary>
    /// Resolves an automation actor for a rule- or job-driven recovery path, e.g.
    /// <c>Rule:42@drain-poison-queue</c> for an AutoReplay rule.
    /// </summary>
    public static RecoveryActor ResolveAutomationActor(string kind, string id, string name)
        => new($"{kind}:{id}@{name}", RecoveryActorKind.Automation);

    /// <summary>
    /// Resolves a system actor for a path with no operator involvement at all, e.g. startup
    /// reconciliation of a stranded claim.
    /// </summary>
    public static RecoveryActor ResolveSystemActor(string context)
        => new($"System:{context}", RecoveryActorKind.System);
}
