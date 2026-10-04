using System.Collections.Concurrent;

namespace ServiceHub.Infrastructure.DlqObserver;

/// <summary>
/// Since when each namespace's whole view of its dead-letter queue has been continuously confirmed (ADR-0018).
/// </summary>
/// <remarks>
/// <b>Kept in memory on purpose, for now.</b> The lasting place for this is a column on the attestation, which needs a
/// migration and so the owner's dated sign-off (ADR-0017). Until then the value starts again whenever ServiceHub
/// restarts — which only ever makes ServiceHub <i>less</i> willing to say "verified": a replay made before the restart
/// is answered "cannot tell". It never makes it more willing.
/// </remarks>
public sealed class DeadLetterViewTracker
{
    private readonly ConcurrentDictionary<(string OwnerId, Guid NamespaceId), DateTimeOffset> _since = new();

    /// <summary>Records that the view was confirmed at <paramref name="now"/>; the first confirmation starts the clock.</summary>
    public DateTimeOffset Confirmed(string ownerId, Guid namespaceId, DateTimeOffset now) => _since.GetOrAdd((ownerId, namespaceId), now);

    /// <summary>The view was lost: whatever it saw before can no longer be relied on.</summary>
    public void Lost(string ownerId, Guid namespaceId) => _since.TryRemove((ownerId, namespaceId), out _);

    /// <summary>Since when the view has been continuously confirmed, or null if it has not been (in this run of ServiceHub).</summary>
    public DateTimeOffset? LiveSince(string ownerId, Guid namespaceId) => _since.TryGetValue((ownerId, namespaceId), out var since) ? since : null;
}
