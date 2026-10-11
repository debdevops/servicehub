using System.Collections.Concurrent;

namespace ServiceHub.Infrastructure.DlqObserver;

/// <summary>
/// Since when each namespace's whole view of its dead-letter queue has been continuously confirmed (ADR-0018).
/// </summary>
/// <remarks>
/// The clock lives in memory for speed and is stored on the attestation (<c>LiveSince</c>, migration 0015) by the Fix Confirmer agent, which
/// also seeds this from it after a restart — but only while the view is still confirmed. A view that went stale or was lost starts again.
/// That can only make ServiceHub <i>less</i> willing to say "verified", never more.
/// </remarks>
public sealed class DeadLetterViewTracker
{
    private readonly ConcurrentDictionary<(string OwnerId, Guid NamespaceId), DateTimeOffset> _since = new();

    /// <summary>Records that the view was confirmed at <paramref name="now"/>; the first confirmation starts the clock.</summary>
    public DateTimeOffset Confirmed(string ownerId, Guid namespaceId, DateTimeOffset now) => _since.GetOrAdd((ownerId, namespaceId), now);

    /// <summary>Starts the clock at a stored time, when this run has none yet (after a restart). Never moves an existing clock.</summary>
    public void Seed(string ownerId, Guid namespaceId, DateTimeOffset since) => _since.TryAdd((ownerId, namespaceId), since);

    /// <summary>The view was lost: whatever it saw before can no longer be relied on.</summary>
    public void Lost(string ownerId, Guid namespaceId) => _since.TryRemove((ownerId, namespaceId), out _);

    /// <summary>Since when the view has been continuously confirmed, or null if it has not been (in this run of ServiceHub).</summary>
    public DateTimeOffset? LiveSince(string ownerId, Guid namespaceId) => _since.TryGetValue((ownerId, namespaceId), out var since) ? since : null;
}
