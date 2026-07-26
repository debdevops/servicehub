using System.IdentityModel.Tokens.Jwt;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Api.Middleware;

/// <summary>
/// Validates <c>Authorization: Bearer &lt;JWT&gt;</c> tokens issued by any standards-compliant
/// OIDC identity provider (Entra ID, Okta, Auth0, Ping, Google Workspace, ...) and sets a
/// per-user <c>OwnerId</c> for tenant isolation — the provider-neutral counterpart to
/// <see cref="EasyAuthMiddleware"/>, which only works behind Azure App Service's built-in auth
/// proxy. This mechanism works identically on Azure, AWS, GCP, Docker Compose, or bare Kestrel:
/// signing keys are fetched and cached from the IdP's OIDC discovery document
/// (<c>{Authority}/.well-known/openid-configuration</c>), so there is nothing platform-specific
/// to trust — the JWT's own signature is the proof of identity, not an upstream-injected header.
/// <para>
/// Must run BEFORE <see cref="ApiKeyAuthenticationMiddleware"/> so a validated OIDC identity
/// takes precedence over (and short-circuits) the legacy SPA-token/API-key path, mirroring
/// <see cref="EasyAuthMiddleware"/>'s position in the pipeline.
/// </para>
/// <para>
/// Disabled by default (<c>Security:Oidc:Enabled</c>). When enabled but no/invalid Bearer token
/// is presented, this middleware passes through without setting <c>OwnerId</c> — exactly like
/// <see cref="EasyAuthMiddleware"/> does for a missing/untrusted header — so a caller can still
/// fall back to the SPA token or a scoped API key. If an upstream middleware already set
/// <c>OwnerId</c> (e.g. Easy Auth succeeded on the same request), this middleware is a no-op —
/// it never overwrites an identity another mechanism already established.
/// </para>
/// <para>
/// A validated token gets full access by default (the same trust EasyAuth/SPA sessions get) —
/// but if the token carries the standard OAuth2 <c>scope</c> claim, those scopes (or role names
/// resolved via <see cref="ApiKeyRoles.Expand"/>) are recorded on <c>context.Items["OidcScopes"]</c>
/// and <c>ScopeAuthorizationFilter</c> enforces them exactly like a scoped API key. Most identity
/// providers don't emit an application-specific <c>scope</c> claim without deliberate app
/// registration configuration, so this is opt-in per deployment, not a behaviour change for
/// existing OIDC deployments.
/// </para>
/// </summary>
public sealed class OidcBearerAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<OidcBearerAuthenticationMiddleware> _logger;
    private readonly OidcOptions _options;
    private readonly IConfigurationManager<OpenIdConnectConfiguration>? _configurationManager;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="OidcBearerAuthenticationMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">Logger instance.</param>
    /// <param name="options">OIDC options, bound from <c>Security:Oidc</c>.</param>
    /// <param name="configurationManager">
    /// Optional injectable OIDC discovery-document manager, for tests. When null and
    /// <see cref="OidcOptions.Enabled"/> is true, a real one is built from
    /// <see cref="OidcOptions.Authority"/>, fetching and auto-rotating signing keys from the
    /// IdP's discovery document.
    /// </param>
    public OidcBearerAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<OidcBearerAuthenticationMiddleware> logger,
        IOptions<OidcOptions> options,
        IConfigurationManager<OpenIdConnectConfiguration>? configurationManager = null)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;

        if (_options.Enabled)
        {
            _configurationManager = configurationManager ?? new ConfigurationManager<OpenIdConnectConfiguration>(
                $"{_options.Authority.TrimEnd('/')}/.well-known/openid-configuration",
                new OpenIdConnectConfigurationRetriever(),
                new HttpDocumentRetriever { RequireHttps = true });
        }
    }

    /// <summary>Invokes the middleware.</summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled
            || _configurationManager is null
            || context.Items.ContainsKey("OwnerId") // an upstream middleware (e.g. EasyAuth) already authenticated this request
            || !context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            await _next(context);
            return;
        }

        var headerValue = authHeader.FirstOrDefault();
        if (string.IsNullOrWhiteSpace(headerValue) ||
            !headerValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var token = headerValue["Bearer ".Length..].Trim();
        var safeMethod = LogRedactor.SanitiseForLog(context.Request.Method);
        var safePath = LogRedactor.SanitiseForLog(context.Request.Path.ToString());

        try
        {
            var oidcConfig = await _configurationManager.GetConfigurationAsync(context.RequestAborted);

            var validationParameters = new TokenValidationParameters
            {
                ValidIssuer = _options.Authority,
                ValidAudience = _options.Audience,
                IssuerSigningKeys = oidcConfig.SigningKeys,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromSeconds(_options.ClockSkewSeconds),
            };

            var principal = _tokenHandler.ValidateToken(token, validationParameters, out _);
            var subject = principal.FindFirst("sub")?.Value
                ?? principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrWhiteSpace(subject))
            {
                _logger.LogWarning(
                    "OIDC token validated but had no 'sub' claim for {Method} {Path} — ignoring",
                    safeMethod, safePath);
                await _next(context);
                return;
            }

            context.Items["OwnerId"] = $"oidc:{subject}";
            context.Items["Authenticated"] = true;
            context.Items["AuthMethod"] = "Oidc";

            // Optional standard OAuth2 'scope' claim (RFC 8693) — a space-delimited string,
            // e.g. "dlq:read dlq:write" or a role name like "Viewer". When present,
            // ScopeAuthorizationFilter enforces it exactly like a scoped API key. When absent
            // (the common case — most IdPs don't emit app-specific scopes without deliberate
            // configuration), OIDC identities keep the full-access trust SPA/EasyAuth already
            // get, unchanged from prior behaviour.
            var scopeClaim = principal.FindFirst("scope")?.Value;
            if (!string.IsNullOrWhiteSpace(scopeClaim))
            {
                var rawScopes = scopeClaim.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (rawScopes.Length > 0)
                {
                    context.Items["OidcScopes"] = ApiKeyRoles.Expand(rawScopes);
                }
            }

            _logger.LogDebug(
                "OIDC authentication successful for {Method} {Path}",
                safeMethod, safePath);
        }
        catch (SecurityTokenException ex)
        {
            // Invalid/expired/wrong-issuer/wrong-audience/bad-signature — fall through and let
            // the SPA token or a scoped API key try instead, exactly like EasyAuthMiddleware
            // does for an untrusted header. Never a hard 401 here; ApiKeyAuthenticationMiddleware
            // owns the final "nothing authenticated this request" rejection.
            _logger.LogWarning(
                "OIDC Bearer token validation failed for {Method} {Path}: {Reason}",
                safeMethod, safePath, ex.GetType().Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Malformed token, unreachable IdP discovery endpoint, etc. — same graceful fallback.
            _logger.LogWarning(ex,
                "OIDC Bearer token processing failed for {Method} {Path}",
                safeMethod, safePath);
        }

        await _next(context);
    }
}
