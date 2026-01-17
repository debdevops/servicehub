namespace ServiceHub.Api.Middleware;

/// <summary>
/// Middleware for API Key authentication.
/// Validates the X-API-KEY header against configured API keys.
/// </summary>
public sealed class ApiKeyAuthenticationMiddleware
{
    private const string ApiKeyHeaderName = "X-API-KEY";

    private readonly RequestDelegate _next;
    private readonly ILogger<ApiKeyAuthenticationMiddleware> _logger;
    private readonly HashSet<string> _validApiKeys;
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

        // Load authentication settings
        _authenticationEnabled = configuration.GetValue("Security:Authentication:Enabled", false);

        // Load valid API keys from configuration
        var apiKeys = configuration.GetSection("Security:Authentication:ApiKeys").Get<string[]>() ?? [];
        _validApiKeys = new HashSet<string>(apiKeys, StringComparer.Ordinal);

        if (_authenticationEnabled && _validApiKeys.Count == 0)
        {
            _logger.LogWarning(
                "API Key authentication is enabled but no API keys are configured. " +
                "All requests will be rejected. Configure Security:Authentication:ApiKeys.");
        }

        if (_authenticationEnabled)
        {
            _logger.LogInformation(
                "API Key authentication enabled with {KeyCount} configured keys",
                _validApiKeys.Count);
        }
        else
        {
            _logger.LogWarning(
                "API Key authentication is DISABLED. Set Security:Authentication:Enabled=true in production.");
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

        // Validate API key
        if (!_validApiKeys.Contains(apiKey))
        {
            _logger.LogWarning(
                "Authentication failed: Invalid API key for {Method} {Path}. Key prefix: {KeyPrefix}",
                context.Request.Method,
                path,
                apiKey.Length > 8 ? $"{apiKey[..8]}..." : "***");

            await WriteForbiddenResponse(context, "Invalid API key.");
            return;
        }

        // API key is valid - continue
        _logger.LogDebug(
            "Authentication successful for {Method} {Path}",
            context.Request.Method,
            path);

        // Store authentication info in HttpContext for potential downstream use
        context.Items["Authenticated"] = true;
        context.Items["AuthMethod"] = "ApiKey";

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
