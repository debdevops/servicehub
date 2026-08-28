using System.Collections.Concurrent;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Process-local, TTL-bounded implementation of <see cref="ICorrelationResultCache"/>.
/// </summary>
public sealed class InMemoryCorrelationResultCache : ICorrelationResultCache
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromHours(24);

    private readonly ConcurrentDictionary<Guid, (CorrelationFinding Finding, DateTimeOffset ExpiresAt)> _entries = new();

    /// <inheritdoc />
    public void Store(IEnumerable<CorrelationFinding> findings)
    {
        ArgumentNullException.ThrowIfNull(findings);

        var expiresAt = DateTimeOffset.UtcNow.Add(EntryLifetime);
        foreach (var finding in findings)
        {
            _entries[finding.Id] = (finding, expiresAt);
        }

        EvictExpired();
    }

    /// <inheritdoc />
    public CorrelationFinding? TryGet(Guid id)
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

        return entry.Finding;
    }

    // Opportunistic sweep on every write, rather than a background timer — this cache is small
    // and low-churn (one detection cycle's worth of findings at a time), so a dedicated sweep
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
