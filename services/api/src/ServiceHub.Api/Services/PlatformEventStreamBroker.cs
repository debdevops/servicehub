using System.Collections.Concurrent;
using System.Threading.Channels;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Services;

/// <summary>
/// Fans out <see cref="PlatformEvent"/>s from the <see cref="IPlatformEventBus"/>
/// to connected Server-Sent Events clients.
/// <para>
/// The bus supports only process-lifetime subscriptions (no unsubscribe) and
/// dispatches subscribers sequentially on a single drain loop, so per-connection
/// <c>bus.Subscribe</c> calls are not possible. Instead this broker is registered
/// as a single catch-all subscriber at startup (next to <c>WebhookDlqSpikeHandler</c>)
/// and maintains its own registry of per-connection bounded channels.
/// <see cref="HandleAsync"/> only ever <c>TryWrite</c>s into those channels, so a
/// slow SSE client can never block the bus — its channel drops oldest instead.
/// </para>
/// <para>
/// Tenant isolation: <see cref="PlatformEvent"/> carries no OwnerId, so an event is
/// visible to a connection when its <see cref="PlatformEvent.Actor"/> equals the
/// connection's owner, or its <see cref="PlatformEvent.NamespaceId"/> belongs to the
/// owner's namespaces (resolved via <see cref="INamespaceRepository.GetByOwnerAsync"/>
/// through a per-lookup DI scope, cached briefly per owner).
/// </para>
/// <para>Lifetime: singleton.</para>
/// </summary>
public sealed class PlatformEventStreamBroker
{
    private const int PerConnectionCapacity = 64;
    private const int MaxConnections = 20;
    private static readonly TimeSpan OwnerNamespaceCacheTtl = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OwnerNamespaceFailureTtl = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan OwnerNamespaceLookupTimeout = TimeSpan.FromSeconds(2);

    private static readonly HashSet<string> StreamedEventTypes = new(StringComparer.Ordinal)
    {
        EventTypes.NamespaceCreated,
        EventTypes.NamespaceDeleted,
        EventTypes.DlqMessageDetected,
        EventTypes.DlqSpikeDetected,
        EventTypes.ReplayCompleted,
        EventTypes.RuleMatched,
        EventTypes.AutoReplayRuleCircuitBreakerTripped,
        EventTypes.BulkOperationCompleted,
        EventTypes.AutonomyGrantTransitioned,
    };

    private readonly ConcurrentDictionary<Guid, Connection> _connections = new();
    private readonly ConcurrentDictionary<string, OwnerNamespaceCacheEntry> _ownerNamespaceCache = new(StringComparer.Ordinal);
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<PlatformEventStreamBroker> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="PlatformEventStreamBroker"/>.
    /// </summary>
    /// <param name="serviceProvider">
    /// Root service provider. Used to create a per-lookup scope for resolving the
    /// scoped <see cref="INamespaceRepository"/> — same pattern as <c>WebhookDlqSpikeHandler</c>.
    /// </param>
    /// <param name="logger">Logger instance.</param>
    public PlatformEventStreamBroker(
        IServiceProvider serviceProvider,
        ILogger<PlatformEventStreamBroker> logger)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Registers a new SSE connection for the given owner.
    /// Returns <c>null</c> when the concurrent-connection cap is reached.
    /// Dispose the returned subscription to unregister the connection.
    /// </summary>
    /// <param name="ownerId">Owner identity of the authenticated caller.</param>
    /// <param name="allowedNamespaceIds">
    /// Optional namespace allow-list from the caller's credential. When non-null, namespace-scoped
    /// events for namespaces outside this set are withheld from this connection even though they
    /// fall within the owner's broader visibility — see <see cref="IsWithinAllowList"/>.
    /// </param>
    public EventStreamSubscription? Register(string ownerId, IReadOnlySet<Guid>? allowedNamespaceIds = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);

        if (_connections.Count >= MaxConnections)
        {
            _logger.LogWarning(
                "SSE connection rejected: {Count} active connections reached the cap of {Max}.",
                _connections.Count,
                MaxConnections);
            return null;
        }

        var channel = Channel.CreateBounded<PlatformEvent>(new BoundedChannelOptions(PerConnectionCapacity)
        {
            // A stalled client must never block the bus drain loop — drop its oldest events.
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleWriter = true,
            SingleReader = true,
        });

        var connectionId = Guid.NewGuid();
        _connections[connectionId] = new Connection(ownerId, allowedNamespaceIds, channel);

        _logger.LogInformation(
            "SSE connection {ConnectionId} registered for owner {OwnerId}. Active connections: {Count}.",
            connectionId,
            LogRedactor.SanitiseForLog(ownerId),
            _connections.Count);

        return new EventStreamSubscription(connectionId, channel.Reader, this);
    }

    /// <summary>
    /// Handles a <see cref="PlatformEvent"/> from the bus and forwards it to every
    /// connection whose owner is allowed to see it. Registered as a catch-all
    /// subscriber; silently ignores event types not relevant to the stream.
    /// </summary>
    /// <param name="platformEvent">The event to fan out.</param>
    /// <param name="cancellationToken">Cancellation token from the bus drain loop.</param>
    public async Task HandleAsync(PlatformEvent platformEvent, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(platformEvent);

        if (!StreamedEventTypes.Contains(platformEvent.EventType))
            return;

        // Namespace membership changed — drop the owner's cached namespace set so
        // events for a just-created namespace become visible without waiting out the TTL.
        if (platformEvent.Category == EventCategories.Namespace && platformEvent.Actor is { Length: > 0 } actor)
        {
            _ownerNamespaceCache.TryRemove(actor, out _);
        }

        if (_connections.IsEmpty)
            return;

        // Cached per owner, not per connection: two connections can share an OwnerId (e.g. two
        // admin keys) but carry different namespace allow-lists, so the allow-list check below
        // must stay outside this cache and be re-evaluated per connection.
        var ownerVisibilityCache = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var connection in _connections.Values)
        {
            if (!ownerVisibilityCache.TryGetValue(connection.OwnerId, out var ownerVisible))
            {
                ownerVisible = await IsVisibleToOwnerAsync(connection.OwnerId, platformEvent, cancellationToken);
                ownerVisibilityCache[connection.OwnerId] = ownerVisible;
            }

            if (ownerVisible && IsWithinAllowList(platformEvent, connection.AllowedNamespaceIds))
            {
                connection.Channel.Writer.TryWrite(platformEvent);
            }
        }
    }

    /// <summary>
    /// Further restricts a namespace-scoped event to a connection's own allow-list, on top of
    /// the owner-wide visibility already established by <see cref="IsVisibleToOwnerAsync"/>.
    /// Events without a <see cref="PlatformEvent.NamespaceId"/> (e.g. actor-only matches) are
    /// never namespace-restricted — the allow-list only narrows namespace-scoped visibility.
    /// </summary>
    private static bool IsWithinAllowList(PlatformEvent platformEvent, IReadOnlySet<Guid>? allowedNamespaceIds) =>
        allowedNamespaceIds is null
        || platformEvent.NamespaceId is not Guid namespaceId
        || allowedNamespaceIds.Contains(namespaceId);

    internal void Unregister(Guid connectionId)
    {
        if (_connections.TryRemove(connectionId, out var connection))
        {
            connection.Channel.Writer.TryComplete();
            _logger.LogInformation(
                "SSE connection {ConnectionId} unregistered. Active connections: {Count}.",
                connectionId,
                _connections.Count);
        }
    }

    private async Task<bool> IsVisibleToOwnerAsync(string ownerId, PlatformEvent platformEvent, CancellationToken cancellationToken)
    {
        if (string.Equals(platformEvent.Actor, ownerId, StringComparison.Ordinal))
            return true;

        if (platformEvent.NamespaceId is not Guid namespaceId)
            return false;

        var ownedNamespaceIds = await GetOwnerNamespaceIdsAsync(ownerId, cancellationToken);
        return ownedNamespaceIds.Contains(namespaceId);
    }

    private async Task<IReadOnlySet<Guid>> GetOwnerNamespaceIdsAsync(string ownerId, CancellationToken cancellationToken)
    {
        if (_ownerNamespaceCache.TryGetValue(ownerId, out var cached) && cached.ExpiresUtc > DateTimeOffset.UtcNow)
            return cached.NamespaceIds;

        // This runs on the bus's sequential drain loop, so the lookup is bounded:
        // a short timeout caps how long one event can stall dispatch, and failures
        // are negative-cached below so a broken DB isn't re-queried per event.
        Result<IReadOnlyList<Namespace>> result;
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(OwnerNamespaceLookupTimeout);
            // Deliberately unfiltered (allowedNamespaceIds: null): this result is cached by
            // OwnerId and shared across every connection for that owner, so it must reflect the
            // owner's full visibility. Per-connection allow-list narrowing happens afterward in
            // IsWithinAllowList, which is NOT cached, so it can't leak between connections that
            // share an OwnerId but carry different allow-lists.
            result = await repository.GetByOwnerAsync(ownerId, allowedNamespaceIds: null, timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            result = Result<IReadOnlyList<Namespace>>.Failure(Error.Timeout(
                "SSE_NAMESPACE_LOOKUP_TIMEOUT",
                $"Namespace lookup exceeded {OwnerNamespaceLookupTimeout.TotalSeconds:0}s."));
        }

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Failed to resolve namespaces for owner {OwnerId} while scoping SSE events: {Error}. " +
                "Falling back to last known set.",
                LogRedactor.SanitiseForLog(ownerId),
                result.Error.Message);

            // Re-stamp the last known set with a short TTL: the cache check above then
            // serves subsequent events without retrying the failing lookup for a few
            // seconds, while the stale-fallback data survives repeated failure cycles.
            var fallback = cached?.NamespaceIds ?? new HashSet<Guid>();
            _ownerNamespaceCache[ownerId] = new OwnerNamespaceCacheEntry(fallback, DateTimeOffset.UtcNow.Add(OwnerNamespaceFailureTtl));
            return fallback;
        }

        var namespaceIds = result.Value.Select(ns => ns.Id).ToHashSet();
        _ownerNamespaceCache[ownerId] = new OwnerNamespaceCacheEntry(namespaceIds, DateTimeOffset.UtcNow.Add(OwnerNamespaceCacheTtl));
        return namespaceIds;
    }

    private sealed record Connection(string OwnerId, IReadOnlySet<Guid>? AllowedNamespaceIds, Channel<PlatformEvent> Channel);

    private sealed record OwnerNamespaceCacheEntry(IReadOnlySet<Guid> NamespaceIds, DateTimeOffset ExpiresUtc);
}

/// <summary>
/// A live SSE connection's view of the platform event stream.
/// Disposing unregisters the connection from the <see cref="PlatformEventStreamBroker"/>
/// and completes the underlying channel.
/// </summary>
public sealed class EventStreamSubscription : IDisposable
{
    private readonly Guid _connectionId;
    private readonly PlatformEventStreamBroker _broker;

    internal EventStreamSubscription(Guid connectionId, ChannelReader<PlatformEvent> reader, PlatformEventStreamBroker broker)
    {
        _connectionId = connectionId;
        _broker = broker;
        Reader = reader;
    }

    /// <summary>Reader over the events queued for this connection.</summary>
    public ChannelReader<PlatformEvent> Reader { get; }

    /// <inheritdoc />
    public void Dispose() => _broker.Unregister(_connectionId);
}
