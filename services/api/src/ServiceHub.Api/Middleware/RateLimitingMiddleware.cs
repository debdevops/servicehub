using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Options;
using ServiceHub.Api.Configuration;

namespace ServiceHub.Api.Middleware;

/// <summary>
/// Simple in-memory rate limiting middleware based on IP address.
/// Provides basic protection against excessive requests.
/// </summary>
public sealed class RateLimitingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<RateLimitingMiddleware> _logger;
    private readonly RateLimitOptions _options;
    private readonly HttpHeadersOptions _headersOptions;
    private readonly ConcurrentDictionary<string, RateLimitEntry> _clients = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="RateLimitingMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="options">The rate limit options.</param>
    /// <param name="headersOptions">The HTTP headers options.</param>
    public RateLimitingMiddleware(
        RequestDelegate next,
        ILogger<RateLimitingMiddleware> logger,
        RateLimitOptions? options = null,
        HttpHeadersOptions? headersOptions = null)
    {
        _next = next;
        _logger = logger;
        _options = options ?? new RateLimitOptions();
        _headersOptions = headersOptions ?? new HttpHeadersOptions();
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Skip rate limiting for health checks
        var path = context.Request.Path.Value ?? string.Empty;
        if (path.Contains("health", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var clientId = GetClientIdentifier(context);
        var now = DateTime.UtcNow;

        CleanupExpiredEntries(now);

        var entry = _clients.GetOrAdd(clientId, _ => new RateLimitEntry(now));

        lock (entry)
        {
            // Reset window if expired
            if (now - entry.WindowStart > _options.WindowDuration)
            {
                entry.WindowStart = now;
                entry.RequestCount = 0;
            }

            entry.RequestCount++;

            if (entry.RequestCount > _options.MaxRequests)
            {
                var retryAfter = _options.WindowDuration - (now - entry.WindowStart);

                _logger.LogWarning(
                    "Rate limit exceeded for client {ClientId}. Requests: {RequestCount}/{MaxRequests}",
                    clientId,
                    entry.RequestCount,
                    _options.MaxRequests);

                context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                context.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
                context.Response.Headers[_headersOptions.RateLimitLimit] = _options.MaxRequests.ToString();
                context.Response.Headers[_headersOptions.RateLimitRemaining] = "0";
                context.Response.Headers[_headersOptions.RateLimitReset] = entry.WindowStart.Add(_options.WindowDuration).ToString("O");

                return;
            }

            // Add rate limit headers
            var remaining = _options.MaxRequests - entry.RequestCount;
            context.Response.Headers[_headersOptions.RateLimitLimit] = _options.MaxRequests.ToString();
            context.Response.Headers[_headersOptions.RateLimitRemaining] = remaining.ToString();
            context.Response.Headers[_headersOptions.RateLimitReset] = entry.WindowStart.Add(_options.WindowDuration).ToString("O");
        }

        await _next(context);
    }

    private static string GetClientIdentifier(HttpContext context)
    {
        // Try X-Forwarded-For header first (for clients behind proxy)
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            // Take the first IP in the chain
            var firstIp = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrEmpty(firstIp))
            {
                return firstIp;
            }
        }

        // Fall back to remote IP
        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private void CleanupExpiredEntries(DateTime now)
    {
        var expiredKeys = _clients
            .Where(kvp => now - kvp.Value.WindowStart > _options.WindowDuration * 2)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _clients.TryRemove(key, out _);
        }
    }

    private sealed class RateLimitEntry
    {
        public DateTime WindowStart { get; set; }
        public int RequestCount { get; set; }

        public RateLimitEntry(DateTime windowStart)
        {
            WindowStart = windowStart;
            RequestCount = 0;
        }
    }
}

/// <summary>
/// Options for rate limiting middleware.
/// </summary>
public sealed class RateLimitOptions
{
    /// <summary>
    /// Gets or sets the maximum number of requests allowed per window.
    /// Default is 100 requests.
    /// </summary>
    public int MaxRequests { get; set; } = 100;

    /// <summary>
    /// Gets or sets the duration of the rate limit window.
    /// Default is 1 minute.
    /// </summary>
    public TimeSpan WindowDuration { get; set; } = TimeSpan.FromMinutes(1);
}

