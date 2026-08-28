using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Process-local, in-memory record of each background worker's last successful cycle —
/// self-observability of the autonomy machinery itself (roadmap §6, cross-cutting foundation
/// item 4): "is <c>AutonomyEvaluationWorker</c> still running, or did it silently stall three
/// days ago?"
/// </summary>
/// <remarks>
/// Deliberately not backed by the database, for the same reason as <see cref="IAnomalyResultCache"/>:
/// the RC1 migration freeze (ADR-0006) forbids any new EF Core migration while active, and a
/// worker's own liveness is inherently a live, in-process fact — the last heartbeat this
/// specific process observed — not a durable record a restart should carry forward. A fresh
/// process has legitimately reported nothing yet; that is a different, better state than a
/// stale timestamp inherited from before a restart.
/// </remarks>
public interface IWorkerHeartbeatStore
{
    /// <summary>
    /// Records (or refreshes) the calling worker's last-successful-cycle timestamp as now.
    /// </summary>
    /// <param name="workerName">The reporting worker's class name.</param>
    /// <param name="expectedInterval">
    /// The worker's own configured cadence, used by the worker-heartbeat health check to
    /// judge staleness. Pass <see langword="null"/> when the worker has no fixed cadence to
    /// judge against right now — e.g. a queue-driven worker between jobs, or a worker that is
    /// disabled by configuration.
    /// </param>
    void RecordHeartbeat(string workerName, TimeSpan? expectedInterval);

    /// <summary>All workers that have reported at least one heartbeat this process lifetime.</summary>
    IReadOnlyDictionary<string, WorkerHeartbeat> GetAll();
}
