using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Events;

/// <summary>
/// In-process implementation of <see cref="IPlatformEventBus"/> backed by a
/// bounded <see cref="Channel{T}"/> and a dedicated <see cref="BackgroundService"/>
/// drain loop.
/// <para>
/// Lifetime: singleton. The channel is process-wide and shared across all
/// request scopes. This mirrors the <c>AuditService</c> pattern already
/// established in this codebase.
/// </para>
/// <para>
/// Reliability guarantees:
/// <list type="bullet">
///   <item>
///     <see cref="PublishAsync"/> is non-blocking. It uses <c>Channel&lt;T&gt;.Writer.TryWrite</c> and returns immediately.
///     If the channel is full the oldest event is dropped (<c>DropOldest</c>)
///     and a warning is logged. Publishers must never be blocked.
///   </item>
///   <item>
///     Subscriber failures are caught, logged at Error level, and swallowed.
///     The drain loop continues to the next event. A failing subscriber never
///     crashes the bus or starves other subscribers.
///   </item>
///   <item>
///     Subscribers are invoked sequentially in registration order.
///     A subscriber that needs to perform long-running work should schedule
///     its own background task internally.
///   </item>
/// </list>
/// </para>
/// </summary>
public sealed class InProcessPlatformEventBus : BackgroundService, IPlatformEventBus
{
    /// <summary>
    /// Maximum number of events that can be held in the channel before
    /// the oldest is dropped. Matches the capacity used by <c>AuditService</c>.
    /// </summary>
    private const int ChannelCapacity = 4096;

    private readonly Channel<PlatformEvent> _channel;
    private readonly List<Func<PlatformEvent, CancellationToken, Task>> _subscribers = [];
    private readonly ILogger<InProcessPlatformEventBus> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="InProcessPlatformEventBus"/>.
    /// </summary>
    /// <param name="logger">Logger for publish overflows and subscriber failures.</param>
    public InProcessPlatformEventBus(ILogger<InProcessPlatformEventBus> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _channel = Channel.CreateBounded<PlatformEvent>(new BoundedChannelOptions(ChannelCapacity)
        {
            // Drop oldest rather than block callers — matching AuditService design rule.
            FullMode = BoundedChannelFullMode.DropOldest,
            // Multiple publishers (controllers + background workers) write concurrently.
            SingleWriter = false,
            // Only the BackgroundService drain loop reads.
            SingleReader = true,
        });
    }

    // ── IPlatformEventBus ─────────────────────────────────────────────────────

    /// <inheritdoc />
    public ValueTask PublishAsync(PlatformEvent platformEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(platformEvent);

        if (!_channel.Writer.TryWrite(platformEvent))
        {
            // Channel full — oldest event dropped by BoundedChannelFullMode.DropOldest.
            // Log a warning so operators can tune capacity if this becomes frequent.
            _logger.LogWarning(
                "PlatformEventBus channel is full (capacity {Capacity}). " +
                "Dropping oldest event to accommodate new event of type {EventType} from {Source}.",
                ChannelCapacity,
                platformEvent.EventType,
                platformEvent.Source);
        }

        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public void Subscribe(Func<PlatformEvent, CancellationToken, Task> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _subscribers.Add(handler);
    }

    // ── BackgroundService ─────────────────────────────────────────────────────

    /// <summary>
    /// Drains the event channel and dispatches each <see cref="PlatformEvent"/>
    /// to all registered subscribers in registration order.
    /// </summary>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "InProcessPlatformEventBus drain loop started. " +
            "Channel capacity: {Capacity}. Registered subscribers: {Count}.",
            ChannelCapacity,
            _subscribers.Count);

        await foreach (var platformEvent in _channel.Reader.ReadAllAsync(stoppingToken))
        {
            await DispatchAsync(platformEvent, stoppingToken);
        }

        _logger.LogInformation("InProcessPlatformEventBus drain loop stopped.");
    }

    // ── Private ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Dispatches a single <see cref="PlatformEvent"/> to every registered subscriber.
    /// Subscriber exceptions are caught and logged; dispatching continues to
    /// the next subscriber regardless of failure.
    /// </summary>
    private async Task DispatchAsync(PlatformEvent platformEvent, CancellationToken stoppingToken)
    {
        if (_subscribers.Count == 0)
        {
            // No-op — bus is wired but no subscribers registered yet (Phase 1).
            return;
        }

        foreach (var subscriber in _subscribers)
        {
            try
            {
                await subscriber(platformEvent, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                // Graceful shutdown — stop dispatching.
                return;
            }
            catch (Exception ex)
            {
                // A failing subscriber must never crash the bus or starve other subscribers.
                _logger.LogError(
                    ex,
                    "PlatformEventBus subscriber threw an unhandled exception " +
                    "while handling event {EventType} (Id: {EventId}). " +
                    "Dispatching continues for remaining subscribers.",
                    platformEvent.EventType,
                    platformEvent.Id);
            }
        }
    }
}
