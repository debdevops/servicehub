using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ServiceHub.Api.Authorization;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Api.Filters;

/// <summary>
/// Authorization filter that enforces scope requirements. Checks if the authenticated API key
/// — or, when present, an OIDC Bearer token's <c>scope</c> claim (see
/// <c>OidcBearerAuthenticationMiddleware</c>) — has the required scope for the endpoint.
/// </summary>
public sealed class ScopeAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly ILogger<ScopeAuthorizationFilter> _logger;
    private readonly bool _authenticationEnabled;

    public ScopeAuthorizationFilter(ILogger<ScopeAuthorizationFilter> logger, IConfiguration configuration)
    {
        _logger = logger;
        _authenticationEnabled = configuration.GetValue("Security:Authentication:Enabled", false);
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        // Skip authorization if authentication is disabled
        if (!_authenticationEnabled)
        {
            return;
        }

        // Check if endpoint requires scope
        var requiredScope = GetRequiredScope(context);
        if (string.IsNullOrEmpty(requiredScope))
        {
            // No scope required
            return;
        }

        // SPA token and EasyAuth (Azure AD) grant unconditional full access — this trust model
        // predates per-request scoping and only ever applied to the single shared browser/Azure
        // identity, not something an operator can restrict per-caller.
        if (context.HttpContext.Items.TryGetValue("AuthMethod", out var authMethod)
            && authMethod is "SpaToken" or "EasyAuth")
        {
            return;
        }

        // OIDC Bearer identities: enforce scopes if the token carried an OAuth2 'scope' claim
        // (see OidcBearerAuthenticationMiddleware), otherwise fall back to the same unconditional
        // full access SPA/EasyAuth get — most identity providers don't emit an app-specific
        // scope claim without deliberate configuration, so this preserves prior behaviour for
        // any OIDC deployment that hasn't opted into scoped tokens.
        if (authMethod is "Oidc")
        {
            if (context.HttpContext.Items.TryGetValue("OidcScopes", out var oidcScopesObj)
                && oidcScopesObj is string[] { Length: > 0 } oidcScopes)
            {
                if (!oidcScopes.Any(scope => ApiKeyScopes.Grants(scope, requiredScope)))
                {
                    _logger.LogWarning(
                        "Authorization failed: OIDC identity lacks required scope {Scope} for {Method} {Path}",
                        requiredScope,
                        LogRedactor.SanitiseForLog(context.HttpContext.Request.Method),
                        LogRedactor.SanitiseForLog(context.HttpContext.Request.Path));

                    context.Result = BuildForbidden(context, requiredScope);
                }

                return;
            }

            return;
        }

        // Get authenticated API key config from middleware
        if (!context.HttpContext.Items.TryGetValue("ApiKeyConfig", out var keyConfigObj) ||
            keyConfigObj is not ApiKeyConfiguration keyConfig)
        {
            // Not authenticated or no API key config available
            _logger.LogWarning(
                "Authorization failed: No API key configuration found for {Method} {Path} requiring scope {Scope}",
                LogRedactor.SanitiseForLog(context.HttpContext.Request.Method),
                LogRedactor.SanitiseForLog(context.HttpContext.Request.Path),
                requiredScope);

            context.Result = new JsonResult(new
            {
                type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                title = "Unauthorized",
                status = 401,
                detail = "Authentication required. Provide a valid X-API-KEY header.",
                correlationId = context.HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown"
            })
            {
                StatusCode = StatusCodes.Status401Unauthorized
            };
            return;
        }

        // Check if API key has required scope
        if (!keyConfig.HasScope(requiredScope))
        {
            _logger.LogWarning(
                "Authorization failed: API key {KeyPrefix} lacks required scope {Scope} for {Method} {Path}",
                keyConfig.GetSafeKey(),
                requiredScope,
                LogRedactor.SanitiseForLog(context.HttpContext.Request.Method),
                LogRedactor.SanitiseForLog(context.HttpContext.Request.Path));

            context.Result = BuildForbidden(context, requiredScope);
            return;
        }

        _logger.LogDebug(
            "Authorization successful: API key {KeyPrefix} has required scope {Scope} for {Method} {Path}",
            keyConfig.GetSafeKey(),
            requiredScope,
            LogRedactor.SanitiseForLog(context.HttpContext.Request.Method),
            LogRedactor.SanitiseForLog(context.HttpContext.Request.Path));

        await Task.CompletedTask;
    }

    private static JsonResult BuildForbidden(AuthorizationFilterContext context, string requiredScope) =>
        new(new
        {
            type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            title = "Forbidden",
            status = 403,
            detail = $"Insufficient permissions. Required scope: {requiredScope}",
            correlationId = context.HttpContext.Items["CorrelationId"]?.ToString() ?? "unknown"
        })
        {
            StatusCode = StatusCodes.Status403Forbidden
        };

    private static string? GetRequiredScope(AuthorizationFilterContext context)
    {
        // EndpointMetadata already contains both controller-level and action-level attributes,
        // ordered controller-first, so LastOrDefault() gives the action-level attribute when one
        // exists and falls back to the controller-level one otherwise — which is exactly the
        // intended precedence.
        //
        // This previously ran FirstOrDefault() and then, if that returned null, LastOrDefault()
        // over the same collection as a "controller-level fallback". The second lookup was
        // unreachable: if FirstOrDefault() found nothing the collection was empty, so
        // LastOrDefault() found nothing either. It read as though controller-level scopes were
        // handled by a separate path when they were not.
        return context.ActionDescriptor.EndpointMetadata
            .OfType<RequireScopeAttribute>()
            .LastOrDefault()
            ?.Scope;
    }
}
