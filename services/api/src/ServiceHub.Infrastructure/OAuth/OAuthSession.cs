namespace ServiceHub.Infrastructure.OAuth;

/// <summary>
/// Internal representation of an OAuth session with full token data.
/// Never serialized or sent to clients — session ID is the only client-visible reference.
/// </summary>
internal sealed class OAuthSession
{
    /// <summary>Cryptographically random session identifier stored in the HttpOnly cookie.</summary>
    public required string SessionId { get; init; }

    /// <summary>The Azure user's principal name (email), e.g. alice@contoso.com.</summary>
    public required string UserPrincipalName { get; init; }

    /// <summary>The Azure tenant ID the user authenticated against.</summary>
    public required string TenantId { get; init; }

    // ── ARM token (management.azure.com) ─────────────────────────────────────

    /// <summary>ARM access token for listing Service Bus namespaces via Azure Resource Manager.</summary>
    public required string ArmAccessToken { get; set; }

    /// <summary>When the ARM access token expires.</summary>
    public required DateTimeOffset ArmTokenExpiry { get; set; }

    // ── Refresh token ─────────────────────────────────────────────────────────

    /// <summary>
    /// OAuth refresh token used to obtain Service Bus tokens and to renew the ARM token.
    /// Updated on every token refresh (rotating refresh tokens).
    /// </summary>
    public required string RefreshToken { get; set; }

    // ── Service Bus token (cached after first use) ────────────────────────────

    /// <summary>Cached Service Bus access token (servicebus.azure.com scope).</summary>
    public string? SbAccessToken { get; set; }

    /// <summary>When the cached Service Bus token expires.</summary>
    public DateTimeOffset SbTokenExpiry { get; set; }

    // ── Session lifetime ──────────────────────────────────────────────────────

    /// <summary>When this session was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Session lifetime. 8 hours from creation.</summary>
    public DateTimeOffset ExpiresAt => CreatedAt.AddHours(8);

    /// <summary>Returns true if this session has exceeded its lifetime.</summary>
    public bool IsExpired => DateTimeOffset.UtcNow > ExpiresAt;
}

/// <summary>
/// Pending PKCE state awaiting code exchange. Expires in 10 minutes.
/// </summary>
internal sealed record PkceState(string CodeVerifier, DateTimeOffset Expiry);
