using System.Collections.Concurrent;
using Microsoft.Extensions.Options;

namespace ServiceHub.Api.Security;

/// <summary>
/// Tracks repeated authentication failures per client IP and locks a client out once it crosses
/// the configured threshold within the window. This is separate from <see cref="Middleware.RateLimitingMiddleware"/>
/// because that limiter runs after <see cref="Middleware.ApiKeyAuthenticationMiddleware"/> (so it
/// can key on the authenticated owner, not just IP) — a bad key never reaches it, leaving API key
/// brute force unthrottled. This class closes that gap without touching pipeline order.
/// </summary>
public sealed class AuthFailureThrottle
{
    // Mirrors RateLimitingMiddleware's capacity/eviction discipline so there is one
    // memory-safety pattern for bounded per-client dictionaries in this codebase, not two.
    private const int MaxTrackedClients = 10_000;
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromSeconds(30);

    private readonly AuthFailureThrottleOptions _options;
    private readonly ConcurrentDictionary<string, (int Count, DateTime WindowStart)> _clients = new();
    private long _lastCleanupTicks;

    public AuthFailureThrottle(IOptions<AuthFailureThrottleOptions> options)
    {
        _options = options.Value;
    }

    /// <summary>
    /// Returns true if <paramref name="clientId"/> has met or exceeded the failure threshold
    /// within the current window, and how long until the window resets.
    /// </summary>
    public bool IsLockedOut(string clientId, out TimeSpan retryAfter)
    {
        var now = DateTime.UtcNow;
        CleanupExpiredEntries(now);

        if (_clients.TryGetValue(clientId, out var entry)
            && now - entry.WindowStart <= _options.Window
            && entry.Count >= _options.Threshold)
        {
            var remaining = _options.Window - (now - entry.WindowStart);
            retryAfter = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
            return true;
        }

        retryAfter = TimeSpan.Zero;
        return false;
    }

    /// <summary>
    /// Records one authentication failure for <paramref name="clientId"/>, starting a fresh
    /// window if the previous one has expired.
    /// </summary>
    public void RecordFailure(string clientId)
    {
        var now = DateTime.UtcNow;
        EvictIfAtCapacity(clientId);

        _clients.AddOrUpdate(
            clientId,
            _ => (1, now),
            (_, existing) => now - existing.WindowStart > _options.Window
                ? (1, now)
                : (existing.Count + 1, existing.WindowStart));
    }

    /// <summary>
    /// Clears any tracked failures for <paramref name="clientId"/> after a successful authentication.
    /// </summary>
    public void RecordSuccess(string clientId)
    {
        _clients.TryRemove(clientId, out _);
    }

    private void EvictIfAtCapacity(string clientId)
    {
        if (_clients.Count < MaxTrackedClients || _clients.ContainsKey(clientId))
        {
            return;
        }

        var toRemove = _clients
            .OrderBy(kvp => kvp.Value.WindowStart)
            .Take(MaxTrackedClients / 10)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            _clients.TryRemove(key, out _);
        }
    }

    private void CleanupExpiredEntries(DateTime now)
    {
        var lastCleanup = Interlocked.Read(ref _lastCleanupTicks);
        if (now.Ticks - lastCleanup < CleanupInterval.Ticks)
        {
            return;
        }

        // Only one request per interval performs the sweep; losers of the race skip it.
        if (Interlocked.CompareExchange(ref _lastCleanupTicks, now.Ticks, lastCleanup) != lastCleanup)
        {
            return;
        }

        var expiredKeys = _clients
            .Where(kvp => now - kvp.Value.WindowStart > _options.Window * 2)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _clients.TryRemove(key, out _);
        }
    }
}

/// <summary>
/// Options for <see cref="AuthFailureThrottle"/>.
/// </summary>
public sealed class AuthFailureThrottleOptions
{
    public const string SectionName = "Security:Authentication:FailureThrottle";

    /// <summary>
    /// Gets or sets the number of failures within <see cref="Window"/> that trigger a lockout.
    /// Default is 10.
    /// </summary>
    public int Threshold { get; set; } = 10;

    /// <summary>
    /// Gets or sets the sliding window over which failures are counted.
    /// Default is 5 minutes.
    /// </summary>
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(5);
}
