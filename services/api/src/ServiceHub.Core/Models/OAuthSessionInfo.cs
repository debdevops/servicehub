namespace ServiceHub.Core.Models;

/// <summary>
/// Public session information for a user authenticated via Azure OAuth.
/// Contains no tokens or secrets — safe to return to clients.
/// </summary>
/// <param name="SessionId">The opaque session identifier (stored in HttpOnly cookie).</param>
/// <param name="UserPrincipalName">The user's Azure UPN (e.g. alice@contoso.com).</param>
/// <param name="TenantId">The Azure tenant ID the user authenticated against.</param>
/// <param name="ExpiresAt">When this session expires (8 hours from sign-in).</param>
public sealed record OAuthSessionInfo(
    string SessionId,
    string UserPrincipalName,
    string TenantId,
    DateTimeOffset ExpiresAt)
{
    /// <summary>Returns true if the session has expired.</summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
}
