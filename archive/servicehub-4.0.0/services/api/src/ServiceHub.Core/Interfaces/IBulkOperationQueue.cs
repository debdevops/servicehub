namespace ServiceHub.Core.Interfaces;

/// <summary>
/// In-process hand-off between the API (which creates jobs) and the background worker
/// (which processes them), plus the live cancellation-signal registry for running jobs.
/// </summary>
/// <remarks>
/// This is intentionally in-process only — consistent with ServiceHub's current single-instance
/// architecture (SQLite, in-process <see cref="IPlatformEventBus"/>). A distributed queue would
/// only be justified if ServiceHub grows a multi-replica deployment story, which is out of scope
/// today; see docs/EXTENDING-PROVIDERS.md's sibling architecture notes. Job durability comes from
/// the persisted <see cref="Entities.BulkOperationJob"/> row, not from this queue — a job that's
/// enqueued but not yet dequeued when the process restarts is picked back up from
/// <see cref="Enums.BulkOperationStatus.Pending"/> rows at worker startup, not replayed from here.
/// </remarks>
public interface IBulkOperationQueue
{
    /// <summary>Signals the worker that a newly created job (in <c>Pending</c> status) is ready to run.</summary>
    void Enqueue(Guid jobId);

    /// <summary>Asynchronously drains queued job IDs; completes when <paramref name="cancellationToken"/> is triggered (host shutdown).</summary>
    IAsyncEnumerable<Guid> DequeueAllAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Registers a fresh <see cref="CancellationToken"/> for a job that is about to start
    /// processing. Must be called by the worker before executing the job.
    /// </summary>
    CancellationToken RegisterRunning(Guid jobId);

    /// <summary>
    /// Signals cancellation for a running job. A no-op if the job isn't currently registered
    /// (e.g. it already finished, or was never started) — the caller falls back to the
    /// persisted job status to decide whether cancellation is meaningful.
    /// </summary>
    void RequestCancellation(Guid jobId);

    /// <summary>Unregisters a job's cancellation token once it reaches a terminal state.</summary>
    void Complete(Guid jobId);
}
