using System.Collections.Concurrent;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.BackgroundServices;

/// <summary>
/// Process-local implementation of <see cref="IWorkerHeartbeatStore"/>. One entry per worker
/// name — a small, fixed key set (one row per registered <see cref="Microsoft.Extensions.Hosting.BackgroundService"/>),
/// so unlike <c>InMemoryAnomalyResultCache</c> and its siblings there is no unbounded growth to
/// evict against.
/// </summary>
public sealed class InMemoryWorkerHeartbeatStore : IWorkerHeartbeatStore
{
    private readonly ConcurrentDictionary<string, WorkerHeartbeat> _heartbeats = new(StringComparer.Ordinal);

    /// <inheritdoc />
    public void RecordHeartbeat(string workerName, TimeSpan? expectedInterval)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workerName);

        _heartbeats[workerName] = new WorkerHeartbeat(workerName, DateTimeOffset.UtcNow, expectedInterval);
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, WorkerHeartbeat> GetAll() => _heartbeats;
}
