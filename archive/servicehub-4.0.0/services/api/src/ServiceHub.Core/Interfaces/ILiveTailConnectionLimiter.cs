namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Bounds the number of concurrent Live Tail SSE connections server-wide, the same way
/// <c>PlatformEventStreamBroker</c> caps concurrent event-stream connections — each open
/// session polls a cloud provider on a short interval for as long as it's held open, so an
/// unbounded number of sessions would translate directly into unbounded provider API load.
/// </summary>
public interface ILiveTailConnectionLimiter
{
    /// <summary>Attempts to reserve a slot. Returns false when the cap has been reached.</summary>
    bool TryAcquire();

    /// <summary>Releases a slot reserved by a prior successful <see cref="TryAcquire"/>.</summary>
    void Release();
}
