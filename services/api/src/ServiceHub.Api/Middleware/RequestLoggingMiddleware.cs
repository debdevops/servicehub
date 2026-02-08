using System.Diagnostics;

namespace ServiceHub.Api.Middleware;

/// <summary>
/// Middleware for logging HTTP request details including method, path, and duration.
/// Skips logging for health check endpoints to reduce noise.
/// </summary>
public sealed class RequestLoggingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RequestLoggingMiddleware> _logger;

    private static readonly HashSet<string> SkippedPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        "/health",
        "/health/ready",
        "/health/live",
        "/api/v1/health",
        "/api/v1/health/ready",
        "/api/v1/health/live"
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestLoggingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    public RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Skip logging for health checks
        if (ShouldSkipLogging(path))
        {
            await _next(context);
            return;
        }

        var correlationId = context.Items["CorrelationId"]?.ToString() ?? "unknown";
        var method = context.Request.Method;
        var queryString = context.Request.QueryString.HasValue ? context.Request.QueryString.Value : string.Empty;

        _logger.LogInformation(
            "Request started: {Method} {Path}{QueryString} | CorrelationId: {CorrelationId}",
            method,
            path,
            queryString,
            correlationId);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await _next(context);
        }
        finally
        {
            stopwatch.Stop();
            var statusCode = context.Response.StatusCode;
            var duration = stopwatch.ElapsedMilliseconds;

            var logLevel = statusCode >= 500 ? LogLevel.Error :
                          statusCode >= 400 ? LogLevel.Warning :
                          LogLevel.Information;

            _logger.Log(logLevel,
                "Request completed: {Method} {Path} | Status: {StatusCode} | Duration: {Duration}ms | CorrelationId: {CorrelationId}",
                method,
                path,
                statusCode,
                duration,
                correlationId);
        }
    }

    private static bool ShouldSkipLogging(string path)
    {
        return SkippedPaths.Contains(path) ||
               path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase) ||
               path.StartsWith("/_", StringComparison.OrdinalIgnoreCase);
    }
}
