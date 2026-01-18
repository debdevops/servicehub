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
    // CRITICAL FIX: Bound dictionary size to prevent DoS via memory exhaustion
    private const int MaxTrackedClients = 10_000;

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

        // CRITICAL FIX: Enforce max clients to prevent unbounded memory growth
        var entry = _clients.GetOrAdd(clientId, id =>
        {
            // If at capacity, evict 10% oldest entries before adding new one
            if (_clients.Count >= MaxTrackedClients)
            {
                var toRemove = _clients
                    .OrderBy(kvp => kvp.Value.WindowStartTicks)
                    .Take(MaxTrackedClients / 10)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in toRemove)
                {
                    _clients.TryRemove(key, out _);
                }

                _logger.LogWarning(
                    "Rate limiter evicted {Count} oldest entries to maintain capacity",
                    toRemove.Count);
            }

            return new RateLimitEntry(now);
        });

        // CRITICAL FIX: Replace lock with async-safe operations to prevent deadlocks
        // Try to reset window if expired
        entry.TryResetIfExpired(_options.WindowDuration, now);

        // Increment request count atomically
        var currentCount = entry.IncrementAndGetCount();

        if (currentCount > _options.MaxRequests)
        {
            var windowStart = entry.WindowStart;
            var retryAfter = _options.WindowDuration - (now - windowStart);

            _logger.LogWarning(
                "Rate limit exceeded for client {ClientId}. Requests: {RequestCount}/{MaxRequests}",
                clientId,
                currentCount,
                _options.MaxRequests);

            context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
            context.Response.Headers.RetryAfter = ((int)retryAfter.TotalSeconds).ToString();
            context.Response.Headers[_headersOptions.RateLimitLimit] = _options.MaxRequests.ToString();
            context.Response.Headers[_headersOptions.RateLimitRemaining] = "0";
            context.Response.Headers[_headersOptions.RateLimitReset] = windowStart.Add(_options.WindowDuration).ToString("O");

            return;
        }

        // Add rate limit headers
        var remaining = _options.MaxRequests - currentCount;
        var resetTime = entry.WindowStart;
        context.Response.Headers[_headersOptions.RateLimitLimit] = _options.MaxRequests.ToString();
        context.Response.Headers[_headersOptions.RateLimitRemaining] = remaining.ToString();
        context.Response.Headers[_headersOptions.RateLimitReset] = resetTime.Add(_options.WindowDuration).ToString("O");

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

    /// <summary>
    /// Thread-safe rate limit entry using Interlocked operations.
    /// CRITICAL FIX: Eliminates async lock to prevent deadlocks under load.
    /// </summary>
    private sealed class RateLimitEntry
    {
        private long _windowStartTicks;
        private int _requestCount;

        public RateLimitEntry(DateTime windowStart)
        {
            _windowStartTicks = windowStart.Ticks;
            _requestCount = 0;
        }

        /// <summary>
        /// Gets the window start time. Thread-safe read.
        /// </summary>
        public DateTime WindowStart => new(Interlocked.Read(ref _windowStartTicks));

        /// <summary>
        /// Exposed for sorting/eviction. Thread-safe read.
        /// </summary>
        public long WindowStartTicks => Interlocked.Read(ref _windowStartTicks);

        /// <summary>
        /// Atomically increments request count and returns new value.
        /// </summary>
        public int IncrementAndGetCount() => Interlocked.Increment(ref _requestCount);

        /// <summary>
        /// Attempts to reset the window if expired. Thread-safe.
        /// Returns true if reset occurred.
        /// </summary>
        public bool TryResetIfExpired(TimeSpan windowDuration, DateTime now)
        {
            var currentWindowStart = new DateTime(Interlocked.Read(ref _windowStartTicks));

            // Check if window has expired
            if (now - currentWindowStart <= windowDuration)
            {
                return false;
            }

            // Try to reset (another thread may have already reset)
            var originalTicks = Interlocked.CompareExchange(
                ref _windowStartTicks,
                now.Ticks,
                currentWindowStart.Ticks);

            // If we successfully updated the window start, reset count
            if (originalTicks == currentWindowStart.Ticks)
            {
                Interlocked.Exchange(ref _requestCount, 0);
                return true;
            }

            return false;
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

