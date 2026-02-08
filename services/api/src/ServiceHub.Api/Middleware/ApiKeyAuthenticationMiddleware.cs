using ServiceHub.Api.Authorization;

namespace ServiceHub.Api.Middleware;

/// <summary>
/// Middleware for API Key authentication with scope-based authorization.
/// Validates the X-API-KEY header and enforces endpoint-level scope requirements.
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware
{
    private const string ApiKeyHeaderName = "X-API-KEY";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly Dictionary<string, ApiKeyConfiguration> _apiKeyLookup;
    private readonly bool _authenticationEnabled;

    private static readonly HashSet<string> BypassPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/health/ready",
        "/health/live",
        "/api/v1/health",
        "/api/v1/health/ready",
        "/api/v1/health/live"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="ApiKeyAuthenticationMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configuration">The configuration.</param>
    public ApiKeyAuthenticationMiddleware(
        RequestDelegate next,
        ILogger<ApiKeyAuthenticationMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;
        _apiKeyLookup = new Dictionary<string, ApiKeyConfiguration>(StringComparer.Ordinal);

        // Load authentication settings
        _authenticationEnabled = configuration.GetValue("Security:Authentication:Enabled", false);

        // Load API keys with scope support
        LoadApiKeys(configuration);

        if (_authenticationEnabled && _apiKeyLookup.Count == 0)
        {
            _logger.LogWarning(
                "API Key authentication is enabled but no API keys are configured. " +
                "All requests will be rejected. Configure Security:Authentication:ApiKeys.");
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
                if (!string.IsNullOrWhiteSpace(key))
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
                if (!string.IsNullOrWhiteSpace(keyConfig.Key))
                {
                    _apiKeyLookup[keyConfig.Key] = keyConfig;
                }
            }
        }
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

        // Bypass authentication for health checks and swagger
        if (ShouldBypassAuthentication(path))
        {
            await _next(context);
            return;
        }

        // Extract API key from header
        if (!context.Request.Headers.TryGetValue(ApiKeyHeaderName, out var apiKeyHeader))
        {
            _logger.LogWarning(
                "Authentication failed: Missing {HeaderName} header for {Method} {Path}",
                ApiKeyHeaderName,
                context.Request.Method,
                path);

            await WriteUnauthorizedResponse(context, "API key is required. Provide the X-API-KEY header.");
            return;
        }

        var apiKey = apiKeyHeader.FirstOrDefault();

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _logger.LogWarning(
                "Authentication failed: Empty {HeaderName} header for {Method} {Path}",
                ApiKeyHeaderName,
                context.Request.Method,
                path);

            await WriteUnauthorizedResponse(context, "API key is required. Provide a valid X-API-KEY header.");
            return;
        }

        // Validate API key exists
        if (!_apiKeyLookup.TryGetValue(apiKey, out var keyConfig))
        {
            _logger.LogWarning(
                "Authentication failed: Invalid API key for {Method} {Path}",
                context.Request.Method,
                path);

            await WriteForbiddenResponse(context, "Invalid API key.");
            return;
        }

        // API key is valid - store config for scope checking
        context.Items["Authenticated"] = true;
        context.Items["AuthMethod"] = "ApiKey";
        context.Items["ApiKeyConfig"] = keyConfig;

        _logger.LogDebug(
            "Authentication successful for {Method} {Path} with key {KeyPrefix}",
            context.Request.Method,
            path,
            keyConfig.GetSafeKey());

        await _next(context);
    }

    private static bool ShouldBypassAuthentication(string path)
    {
        // Exact match for known paths
        if (BypassPaths.Contains(path))
        {
            return true;
        }

        // Swagger UI and related endpoints
        if (path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // OpenAPI spec endpoint
        if (path.Contains("/swagger/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

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
}
