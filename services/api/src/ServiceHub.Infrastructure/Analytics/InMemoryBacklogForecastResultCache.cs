using System.Collections.Concurrent;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Process-local, TTL-bounded implementation of <see cref="IBacklogForecastResultCache"/>.
/// </summary>
public sealed class InMemoryBacklogForecastResultCache : IBacklogForecastResultCache
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<Guid, (BacklogForecast Forecast, DateTimeOffset ExpiresAt)> _entries = new();

    /// <inheritdoc />
    public void Store(IEnumerable<BacklogForecast> forecasts)
    {
        ArgumentNullException.ThrowIfNull(forecasts);

        var expiresAt = DateTimeOffset.UtcNow.Add(EntryLifetime);
        foreach (var forecast in forecasts)
        {
            _entries[forecast.Id] = (forecast, expiresAt);
        }

        EvictExpired();
    }

    /// <inheritdoc />
    public BacklogForecast? TryGet(Guid id)
    {
        if (!_entries.TryGetValue(id, out var entry))
        {
            return null;
        }

        if (entry.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            _entries.TryRemove(id, out _);
            return null;
        }

        return entry.Forecast;
    }

    // Opportunistic sweep on every write, rather than a background timer — this cache is small
    // and low-churn (one forecast cycle's worth of entities at a time), so a dedicated sweep
    // loop would be more machinery than the data it protects.
    private void EvictExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (id, entry) in _entries)
        {
            if (entry.ExpiresAt <= now)
            {
                _entries.TryRemove(id, out _);
            }
        }
    }
}
