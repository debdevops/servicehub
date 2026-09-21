using ServiceHub.Core.Events;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Internal in-process event bus for <see cref="PlatformEvent"/> instances.
/// <para>
/// Design constraints:
/// <list type="bullet">
///   <item>
///     <see cref="PublishAsync"/> must never block the calling thread.
///     Implementations must enqueue and return immediately.
///   </item>
///   <item>
///     Subscriber failures must never crash the bus or affect other subscribers.
///     Implementations must catch, log, and swallow handler exceptions.
///   </item>
///   <item>
///     This interface owns no transport logic. It is a pure in-process contract.
///     It is not backed by Azure Service Bus, Kafka, RabbitMQ, or any external broker.
///   </item>
///   <item>
///     <see cref="Subscribe"/> is intended to be called once at application startup
///     by the DI composition root before any events are published.
///   </item>
/// </list>
/// </para>
/// </summary>
public interface IPlatformEventBus
{
    /// <summary>
    /// Enqueues a <see cref="PlatformEvent"/> for asynchronous dispatch to all
    /// registered subscribers.
    /// <para>
    /// This method must be non-blocking. It must not await subscriber execution.
    /// Callers should invoke it after the domain state-change has been committed
    /// (publish-after-commit rule).
    /// </para>
    /// </summary>
    /// <param name="platformEvent">The event to publish. Must not be null.</param>
    /// <param name="cancellationToken">
    /// Cancellation token. Used only for graceful shutdown; does not cancel
    /// in-flight dispatch.
    /// </param>
    ValueTask PublishAsync(PlatformEvent platformEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers a subscriber handler that will be invoked for every
    /// <see cref="PlatformEvent"/> dequeued by the bus.
    /// <para>
    /// Subscribers are responsible for filtering by <see cref="PlatformEvent.EventType"/>
    /// if they are interested in only a subset of events.
    /// </para>
    /// <para>
    /// Handlers are invoked sequentially in registration order within the drain loop.
    /// Long-running handlers should schedule their own background work internally.
    /// </para>
    /// </summary>
    /// <param name="handler">
    /// An asynchronous delegate that processes a <see cref="PlatformEvent"/>.
    /// Must not throw unhandled exceptions; the bus will log and continue if it does.
    /// </param>
    void Subscribe(Func<PlatformEvent, CancellationToken, Task> handler);
}
