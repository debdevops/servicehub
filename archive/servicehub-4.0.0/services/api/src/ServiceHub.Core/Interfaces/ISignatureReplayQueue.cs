namespace ServiceHub.Core.Interfaces;

/// <summary>
/// In-process hand-off between the API (which creates signature-replay jobs) and
/// <c>SignatureReplayWorker</c> (which processes them), plus the live cancellation-signal
/// registry for running jobs. Mirrors <see cref="IBulkOperationQueue"/> exactly.
/// </summary>
/// <remarks>
/// Job durability comes from the persisted <see cref="Entities.SignatureReplayJob"/> row, not
/// from this queue — a job that's enqueued but not yet dequeued when the process restarts is
/// picked back up from <see cref="Enums.BulkOperationStatus.Pending"/> rows at worker startup,
/// not replayed from here.
/// </remarks>
public interface ISignatureReplayQueue
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
