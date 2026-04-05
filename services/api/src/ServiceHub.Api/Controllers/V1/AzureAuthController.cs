using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceHub.Api.Controllers;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Configuration;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Azure OAuth 2.0 user-delegated authentication controller.
///
/// Implements the Authorization Code + PKCE flow so users can sign in with their own
/// Microsoft identity. No connection strings or SAS keys are ever required.
///
/// Flow:
///   1. GET  /api/v1/auth/azure/sign-in        → returns Azure authorization URL
///   2. [User navigates to Azure login in browser]
///   3. GET  /api/v1/auth/azure/callback        → Azure redirects here with ?code=&amp;state=
///   4. GET  /api/v1/auth/azure/namespaces      → lists user's Service Bus namespaces via ARM
///   5. POST /api/v1/namespaces (authType=UserDelegated) → saves chosen namespace
///   6. DELETE /api/v1/auth/azure/session       → sign out
/// </summary>
[Route("api/v1/auth/azure")]
[Tags("Azure Authentication")]
public sealed class AzureAuthController : ApiControllerBase
{
    private const string SessionCookieName = "servicehub_oauth_session";

    private readonly IOAuthService _oauthService;
    private readonly OAuthOptions _oauthOptions;
    private readonly ILogger<AzureAuthController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureAuthController"/> class.
    /// </summary>
    /// <param name="oauthService">The OAuth service.</param>
    /// <param name="oauthOptions">The OAuth configuration options.</param>
    /// <param name="logger">The logger.</param>
    public AzureAuthController(
        IOAuthService oauthService,
        IOptions<OAuthOptions> oauthOptions,
        ILogger<AzureAuthController> logger)
    {
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _oauthOptions = oauthOptions.Value ?? throw new ArgumentNullException(nameof(oauthOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── GET /api/v1/auth/azure/status ────────────────────────────────────────

    /// <summary>
    /// Returns the current Azure sign-in status for the calling session.
    /// Safe to poll from the frontend to check if the user is authenticated.
    /// </summary>
    /// <response code="200">Status returned (isConfigured, isSignedIn, userPrincipalName).</response>
    [HttpGet("status")]
    [ProducesResponseType(typeof(AzureAuthStatusResponse), StatusCodes.Status200OK)]
    public IActionResult GetStatus()
    {
        var sessionId = Request.Cookies[SessionCookieName];
        var sessionInfo = sessionId is not null ? _oauthService.GetSessionInfo(sessionId) : null;

        return Ok(new AzureAuthStatusResponse(
            IsConfigured: _oauthService.IsConfigured,
            IsSignedIn: sessionInfo is not null,
            UserPrincipalName: sessionInfo?.UserPrincipalName,
            TenantId: sessionInfo?.TenantId,
            ExpiresAt: sessionInfo?.ExpiresAt));
    }

    // ── GET /api/v1/auth/azure/sign-in ───────────────────────────────────────

    /// <summary>
    /// Generates an Azure OAuth 2.0 authorization URL for the user to sign in with.
    /// The frontend should redirect the browser (window.location.href) to the returned URL.
    /// PKCE code_verifier and CSRF state are stored server-side — nothing sensitive goes to the browser.
    /// </summary>
    /// <response code="200">Authorization URL returned.</response>
    /// <response code="503">OAuth is not configured on this ServiceHub instance.</response>
    [HttpGet("sign-in")]
    [ProducesResponseType(typeof(AzureSignInUrlResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public IActionResult GetSignInUrl()
    {
        if (!_oauthService.IsConfigured)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable,
                new { detail = "Azure OAuth sign-in is not configured on this ServiceHub instance. Contact your administrator to set up the App Registration." });
        }

        var (authorizationUrl, _) = _oauthService.GenerateSignInUrl();
        return Ok(new AzureSignInUrlResponse(AuthorizationUrl: authorizationUrl));
    }

    // ── GET /api/v1/auth/azure/callback ──────────────────────────────────────

    /// <summary>
    /// OAuth 2.0 callback endpoint. Azure redirects the user here after sign-in.
    /// Exchanges the authorization code for tokens, creates a session, sets the session cookie,
    /// and redirects the user back to the ServiceHub frontend Connect page.
    ///
    /// This endpoint must be registered as a Redirect URI in your Azure App Registration.
    /// Parameters <c>code</c> and <c>state</c> are provided automatically by Azure.
    /// </summary>
    /// <param name="code">Authorization code from Azure (used once; expires in ~60 seconds).</param>
    /// <param name="state">CSRF state token for verification (bound to PKCE verifier).</param>
    /// <param name="error">Error code from Azure (present on failure).</param>
    /// <param name="error_description">Human-readable error from Azure.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback(
        string? code,
        string? state,
        string? error,
        string? error_description,
        CancellationToken cancellationToken = default)
    {
        var frontendBase = _oauthOptions.FrontendBaseUrl.TrimEnd('/');

        // Handle Azure-side errors (e.g. user denied consent)
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning("Azure OAuth error on callback: {Error} — {Description}", error, error_description);
            var msg = Uri.EscapeDataString(error_description ?? "Azure sign-in was cancelled or failed.");
            return Redirect($"{frontendBase}/connect?tab=entra&auth=error&msg={msg}");
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            return Redirect($"{frontendBase}/connect?tab=entra&auth=error&msg=Missing+authorization+code");
        }

        var result = await _oauthService.ExchangeCodeAsync(code, state, cancellationToken);
        if (result.IsFailure)
        {
            var msg = Uri.EscapeDataString(result.Error.Message);
            return Redirect($"{frontendBase}/connect?tab=entra&auth=error&msg={msg}");
        }

        var sessionId = result.Value;

        // Set HttpOnly session cookie — browser cannot read the value via JavaScript
        var isHttps = Request.IsHttps || (Request.Headers.TryGetValue("X-Forwarded-Proto", out var proto) &&
                      proto == "https");

        Response.Cookies.Append(SessionCookieName, sessionId, new CookieOptions
        {
            HttpOnly = true,
            Secure = isHttps,
            SameSite = SameSiteMode.Lax, // Lax required for OAuth redirect flow
            MaxAge = TimeSpan.FromHours(8),
            Path = "/",
        });

        _logger.LogInformation("OAuth session established, redirecting to frontend");

        return Redirect($"{frontendBase}/connect?tab=entra&auth=success");
    }

    // ── GET /api/v1/auth/azure/namespaces ─────────────────────────────────────

    /// <summary>
    /// Lists the signed-in user's Azure Service Bus namespaces across all their subscriptions.
    /// Uses the Azure Resource Manager API with the user's delegated ARM access token.
    /// No namespace secrets or SAS keys are returned — only metadata (name, hostname, location, tier).
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="200">List of Service Bus namespaces.</response>
    /// <response code="401">No active session or session expired.</response>
    [HttpGet("namespaces")]
    [ProducesResponseType(typeof(IReadOnlyList<AzureNamespaceInfo>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ListNamespaces(CancellationToken cancellationToken = default)
    {
        var sessionId = Request.Cookies[SessionCookieName];
        if (string.IsNullOrEmpty(sessionId))
        {
            return Unauthorized(new { detail = "Please sign in with Azure first." });
        }

        var result = await _oauthService.ListNamespacesAsync(sessionId, cancellationToken);
        if (result.IsFailure)
        {
            if (result.Error.Code.Contains("SessionNotFound", StringComparison.OrdinalIgnoreCase))
                return Unauthorized(new { detail = result.Error.Message });

            return StatusCode(StatusCodes.Status502BadGateway,
                new { detail = result.Error.Message });
        }

        return Ok(result.Value);
    }

    // ── DELETE /api/v1/auth/azure/session ─────────────────────────────────────

    /// <summary>
    /// Signs out the current Azure session: revokes the in-memory session and clears the session cookie.
    /// After calling this endpoint, the user must sign in again to use Entra ID connections.
    /// </summary>
    /// <response code="204">Session revoked.</response>
    [HttpDelete("session")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public new IActionResult SignOut()
    {
        var sessionId = Request.Cookies[SessionCookieName];
        if (!string.IsNullOrEmpty(sessionId))
        {
            _oauthService.RevokeSession(sessionId);
        }

        Response.Cookies.Delete(SessionCookieName, new CookieOptions { Path = "/" });

        return NoContent();
    }
}

// ── Response records ──────────────────────────────────────────────────────────

/// <summary>Current Azure sign-in status for the calling browser session.</summary>
/// <param name="IsConfigured">True if OAuth is configured on this ServiceHub instance.</param>
/// <param name="IsSignedIn">True if the user has an active Azure session.</param>
/// <param name="UserPrincipalName">UPN of the signed-in user (e.g. alice@contoso.com). Null if not signed in.</param>
/// <param name="TenantId">Azure tenant ID. Null if not signed in.</param>
/// <param name="ExpiresAt">When the current session expires. Null if not signed in.</param>
public sealed record AzureAuthStatusResponse(
    bool IsConfigured,
    bool IsSignedIn,
    string? UserPrincipalName,
    string? TenantId,
    DateTimeOffset? ExpiresAt);

/// <summary>Azure OAuth authorization URL for the user to navigate to.</summary>
/// <param name="AuthorizationUrl">The full Microsoft login URL including PKCE and CSRF parameters.</param>
public sealed record AzureSignInUrlResponse(string AuthorizationUrl);
