using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.LiveTail;

/// <inheritdoc cref="ILiveTailConnectionLimiter" />
/// <remarks>Singleton — the counter must be shared across every request.</remarks>
public sealed class LiveTailConnectionLimiter : ILiveTailConnectionLimiter
{
    // Matches PlatformEventStreamBroker.MaxConnections — the same order-of-magnitude cap on
    // concurrent long-lived, provider-polling connections server-wide.
    private const int MaxConcurrentSessions = 20;

    private int _count;

    /// <inheritdoc />
    public bool TryAcquire()
    {
        var updated = Interlocked.Increment(ref _count);
        if (updated <= MaxConcurrentSessions)
        {
            return true;
        }

        Interlocked.Decrement(ref _count);
        return false;
    }

    /// <inheritdoc />
    public void Release() => Interlocked.Decrement(ref _count);
}
