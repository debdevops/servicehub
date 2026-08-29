using System.Collections.Concurrent;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Process-local, TTL-bounded implementation of <see cref="IExternalSignalCorrelationCache"/> —
/// structurally identical to <see cref="InMemoryCorrelationResultCache"/>, applied to C3.
/// </summary>
public sealed class InMemoryExternalSignalCorrelationCache : IExternalSignalCorrelationCache
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<Guid, (ExternalSignalCorrelation Correlation, DateTimeOffset ExpiresAt)> _entries = new();

    /// <inheritdoc />
    public void Store(IEnumerable<ExternalSignalCorrelation> correlations)
    {
        ArgumentNullException.ThrowIfNull(correlations);

        var expiresAt = DateTimeOffset.UtcNow.Add(EntryLifetime);
        foreach (var correlation in correlations)
        {
            _entries[correlation.Id] = (correlation, expiresAt);
        }

        EvictExpired();
    }

    /// <inheritdoc />
    public ExternalSignalCorrelation? TryGet(Guid id)
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

        return entry.Correlation;
    }

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
