using System.Collections.Concurrent;
using System.Threading.Channels;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.SignatureReplay;

/// <summary>
/// Singleton, in-process implementation of <see cref="ISignatureReplayQueue"/>. Mirrors
/// <c>BulkOperationQueue</c> exactly: an unbounded channel hands job IDs from API requests to
/// <c>SignatureReplayWorker</c>, and a <see cref="ConcurrentDictionary{TKey,TValue}"/> of
/// <see cref="CancellationTokenSource"/> lets a cancel request take effect immediately (between
/// messages) rather than waiting for the worker's next database poll.
/// </summary>
public sealed class SignatureReplayQueue : ISignatureReplayQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _running = new();

    /// <inheritdoc />
    public void Enqueue(Guid jobId)
    {
        // Unbounded writer — TryWrite always succeeds unless the channel was completed.
        _channel.Writer.TryWrite(jobId);
    }

    /// <inheritdoc />
    public IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken)
        => _channel.Reader.ReadAllAsync(cancellationToken);

    /// <inheritdoc />
    public CancellationToken RegisterRunning(Guid jobId)
    {
        var cts = new CancellationTokenSource();
        _running[jobId] = cts;
        return cts.Token;
    }

    /// <inheritdoc />
    public void RequestCancellation(Guid jobId)
    {
        if (_running.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
        }
    }

    /// <inheritdoc />
    public void Complete(Guid jobId)
    {
        if (_running.TryRemove(jobId, out var cts))
        {
            cts.Dispose();
        }
    }
}
