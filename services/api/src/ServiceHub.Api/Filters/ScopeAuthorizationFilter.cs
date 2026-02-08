using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using ServiceHub.Api.Authorization;

namespace ServiceHub.Api.Filters;

/// <summary>
/// Authorization filter that enforces API key scope requirements.
/// Checks if the authenticated API key has the required scope for the endpoint.
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

        // Get authenticated API key config from middleware
        if (!context.HttpContext.Items.TryGetValue("ApiKeyConfig", out var keyConfigObj) ||
            keyConfigObj is not ApiKeyConfiguration keyConfig)
        {
            // Not authenticated or no API key config available
            _logger.LogWarning(
                "Authorization failed: No API key configuration found for {Method} {Path} requiring scope {Scope}",
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path,
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
                context.HttpContext.Request.Method,
                context.HttpContext.Request.Path);

            context.Result = new JsonResult(new
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
            return;
        }

        _logger.LogDebug(
            "Authorization successful: API key {KeyPrefix} has required scope {Scope} for {Method} {Path}",
            keyConfig.GetSafeKey(),
            requiredScope,
            context.HttpContext.Request.Method,
            context.HttpContext.Request.Path);

        await Task.CompletedTask;
    }

    private static string? GetRequiredScope(AuthorizationFilterContext context)
    {
        // Check action-level attribute first
        var actionAttribute = context.ActionDescriptor.EndpointMetadata
            .OfType<RequireScopeAttribute>()
            .FirstOrDefault();

        if (actionAttribute != null)
        {
            return actionAttribute.Scope;
        }

        // Check controller-level attribute
        var controllerAttribute = context.ActionDescriptor.EndpointMetadata
            .OfType<RequireScopeAttribute>()
            .LastOrDefault();

        return controllerAttribute?.Scope;
    }
}
