using System.Security.Cryptography;
using System.Text;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Security;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Api.Middleware;

/// <summary>
/// Middleware for API Key authentication with scope-based authorization.
/// Validates the X-API-KEY header and enforces endpoint-level scope requirements.
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware
{
    private const string ApiKeyHeaderName = "X-API-KEY";
    private const string SpaTokenHeaderName = "X-SPA-Token";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly Dictionary<string, ApiKeyConfiguration> _apiKeyLookup;
    private readonly bool _authenticationEnabled;
    private readonly SpaTokenProvider? _spaTokenProvider;

    private static readonly HashSet<string> BypassPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/health/ready",
        "/health/live",
        "/health/version",
        "/health/status",
        "/api/v1/health",
        "/api/v1/health/ready",
        "/api/v1/health/live",
        "/api/v1/health/version",
        "/api/v1/health/status",
        "/api/health/version",
        "/api/health/status"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyAuthenticationMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="spaTokenProvider">Optional SPA token provider for co-hosted browser auth.</param>
    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyAuthenticationMiddleware> logger,
        IConfiguration configuration,
        SpaTokenProvider? spaTokenProvider = null)
    {
        _next = next;
        _logger = logger;
        _spaTokenProvider = spaTokenProvider;
        _apiKeyLookup = new Dictionary<string, ApiKeyConfiguration>(StringComparer.Ordinal);

        // Load authentication settings
        _authenticationEnabled = configuration.GetValue("Security:Authentication:Enabled", false);

        // Load API keys with scope support
        LoadApiKeys(configuration);

        if (_authenticationEnabled && _apiKeyLookup.Count == 0 && (_spaTokenProvider is null || !_spaTokenProvider.IsEnabled))
        {
            _logger.LogWarning(
                "API Key authentication is enabled but no API keys are configured and SPA token is disabled. " +
                "All API requests will be rejected. Configure Security:Authentication:ApiKeys.");
        }

        if (_authenticationEnabled)
        {
            _logger.LogInformation(
                "API Key authentication enabled with {KeyCount} configured keys (scope-aware)",
                _apiKeyLookup.Count);
        }
        else
        {
            _logger.LogWarning(
                "API Key authentication is DISABLED. Set Security:Authentication:Enabled=true in production.");
        }
    }

    private void LoadApiKeys(IConfiguration configuration)
    {
        // Support both simple string array (backward compatible) and scoped keys
        var simpleKeys = configuration.GetSection("Security:Authentication:ApiKeys").Get<string[]>();
        if (simpleKeys != null && simpleKeys.Length > 0)
        {
            foreach (var key in simpleKeys)
            {
                if (!string.IsNullOrWhiteSpace(key) && !IsPlaceholderKey(key))
                {
                    // Simple keys have admin scope (backward compatibility)
                    _apiKeyLookup[key] = new ApiKeyConfiguration
                    {
                        Key = key,
                        Scopes = null, // null = admin
                        Description = "Legacy admin key"
                    };
                }
            }
        }

        // Load scoped keys from ScopedApiKeys section
        var scopedKeys = configuration.GetSection("Security:Authentication:ScopedApiKeys")
            .Get<ApiKeyConfiguration[]>();

        if (scopedKeys != null)
        {
            foreach (var keyConfig in scopedKeys)
            {
                if (!string.IsNullOrWhiteSpace(keyConfig.Key) && !IsPlaceholderKey(keyConfig.Key))
                {
                    _apiKeyLookup[keyConfig.Key] = keyConfig;
                }
                else if (!string.IsNullOrWhiteSpace(keyConfig.Key) && IsPlaceholderKey(keyConfig.Key))
                {
                    _logger.LogWarning(
                        "Skipping placeholder API key '{Description}' — Key Vault may not be configured",
                        keyConfig.Description ?? "unknown");
                }
            }
        }
    }

    /// <summary>
    /// Detects placeholder key values that should never be accepted for authentication.
    /// These are config-file stubs meant to be overridden by Key Vault at startup.
    /// </summary>
    private static bool IsPlaceholderKey(string key)
    {
        return key.StartsWith("REPLACED_BY_KEYVAULT", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("SET_VIA_", StringComparison.OrdinalIgnoreCase)
            || key.StartsWith("CHANGE_THIS", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Skip authentication if disabled
        if (!_authenticationEnabled)
        {
            await _next(context);
            return;
        }

        var path = context.Request.Path.Value ?? string.Empty;

        // Bypass authentication for health checks
        if (ShouldBypassAuthentication(path))
        {
            await _next(context);
            return;
        }

        // Try SPA token first (co-hosted browser requests)
        if (_spaTokenProvider is { IsEnabled: true }
            && context.Request.Headers.TryGetValue(SpaTokenHeaderName, out var spaTokenHeader))
        {
            var spaToken = spaTokenHeader.FirstOrDefault();
            if (_spaTokenProvider.ValidateToken(spaToken))
            {
                context.Items["Authenticated"] = true;
                context.Items["AuthMethod"] = "SpaToken";
                // SPA token identifies the instance admin — all SPA sessions share the SPA owner scope.
                context.Items["OwnerId"] = Namespace.SpaOwnerId;

                _logger.LogDebug(
                    "SPA token authentication successful for {Method} {Path}",
                    LogRedactor.SanitiseForLog(context.Request.Method),
                    LogRedactor.SanitiseForLog(path));

                await _next(context);
                return;
            }

            _logger.LogWarning(
                "SPA token validation failed for {Method} {Path}",
                LogRedactor.SanitiseForLog(context.Request.Method),
                LogRedactor.SanitiseForLog(path));
        }

        // Fall through to API key authentication
        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader)
            || string.IsNullOrWhiteSpace(apiKeyHeader.FirstOrDefault()))
        {
            _logger.LogWarning(
                "Authentication failed: No valid credential for {Method} {Path}",
                LogRedactor.SanitiseForLog(context.Request.Method),
                LogRedactor.SanitiseForLog(path));

            await WriteUnauthorizedResponse(context, "Authentication required. Provide X-API-KEY or access via the ServiceHub UI.");
            return;
        }

        var apiKey = apiKeyHeader.FirstOrDefault()!;

        // Validate API key exists
        if (!_apiKeyLookup.TryGetValue(apiKey, out var keyConfig))
        {
            _logger.LogWarning(
                "Authentication failed: Invalid API key for {Method} {Path}",
            LogRedactor.SanitiseForLog(context.Request.Method),
            LogRedactor.SanitiseForLog(path));

            await WriteForbiddenResponse(context, "Invalid API key.");
            return;
        }

        // API key is valid - store config for scope checking
        context.Items["Authenticated"] = true;
        context.Items["AuthMethod"] = "ApiKey";
        context.Items["ApiKeyConfig"] = keyConfig;
        // Tenant isolation: admin keys (no explicit scopes) share the SPA owner scope so they
        // can see the same namespaces as the browser. Scoped keys get a deterministic owner ID
        // derived from the key itself, isolating their namespace pool from other callers.
        context.Items["OwnerId"] = keyConfig.IsAdminKey
            ? Namespace.SpaOwnerId
            : ComputeScopedOwnerId(apiKey);

        _logger.LogDebug(
            "Authentication successful for {Method} {Path} with key {KeyPrefix}",
            LogRedactor.SanitiseForLog(context.Request.Method),
            LogRedactor.SanitiseForLog(path),
            keyConfig.GetSafeKey());

        await _next(context);
    }

    private static bool ShouldBypassAuthentication(string path)
    {
        // Health check paths must remain accessible for load balancer probes
        if (BypassPaths.Contains(path))
            return true;

        // Only enforce authentication on API routes (/api/*).
        // Static files (JS, CSS, images), index.html, and SPA fallback routes
        // must pass through unauthenticated so the browser can load the app
        // and obtain the SPA token injected into the HTML.
        if (!path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static async Task WriteUnauthorizedResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.ContentType = "application/json";

        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";

        var response = new
        {
            type = "https://tools.ietf.org/html/rfc7235#section-3.1",
            title = "Unauthorized",
            status = 401,
            detail = message,
            correlationId
        };

        await context.Response.WriteAsJsonAsync(response);
    }

    private static async Task WriteForbiddenResponse(HttpContext context, string message)
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        context.Response.ContentType = "application/json";

        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";

        var response = new
        {
            type = "https://tools.ietf.org/html/rfc7231#section-6.5.3",
            title = "Forbidden",
            status = 403,
            detail = message,
            correlationId
        };

        await context.Response.WriteAsJsonAsync(response);
    }

    /// <summary>
    /// Derives a stable, short owner ID from a scoped API key using SHA-256.
    /// The hash is safe to store in repository data — it reveals nothing about the key value
    /// but produces a consistent identity string per unique key.
    /// </summary>
    private static string ComputeScopedOwnerId(string apiKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        // Use first 12 bytes → 24 hex chars. Collision probability is negligible
        // for the number of API keys any single ServiceHub instance will have.
        return "key_" + Convert.ToHexString(hash[..12]).ToLowerInvariant();
    }
}
