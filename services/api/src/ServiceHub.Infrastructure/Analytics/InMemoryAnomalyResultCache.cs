using System.Collections.Concurrent;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Process-local, TTL-bounded implementation of <see cref="IAnomalyResultCache"/>.
/// </summary>
public sealed class InMemoryAnomalyResultCache : IAnomalyResultCache
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<Guid, (Anomaly Anomaly, DateTimeOffset ExpiresAt)> _entries = new();

    /// <inheritdoc />
    public void Store(IEnumerable<Anomaly> anomalies)
    {
        ArgumentNullException.ThrowIfNull(anomalies);

        var expiresAt = DateTimeOffset.UtcNow.Add(EntryLifetime);
        foreach (var anomaly in anomalies)
        {
            _entries[anomaly.Id] = (anomaly, expiresAt);
        }

        EvictExpired();
    }

    /// <inheritdoc />
    public Anomaly? TryGet(Guid id)
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

        return entry.Anomaly;
    }

    // Opportunistic sweep on every write, rather than a background timer — this cache is small
    // and low-churn (one detection cycle's worth of anomalies at a time), so a dedicated sweep
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
