namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Executes a single, already-persisted <see cref="Entities.SignatureReplayJob"/>: loads its
/// snapshotted member messages, replays each one via <see cref="IMessageOperationsService"/>,
/// persists progress as it goes, and records the terminal outcome. Mirrors
/// <see cref="IBulkOperationExecutor"/>'s shape exactly.
/// </summary>
public interface ISignatureReplayExecutor
{
    /// <summary>
    /// Runs the job identified by <paramref name="jobId"/> to completion, cancellation, or
    /// failure. Never throws for expected per-message failures — those are recorded on the job
    /// and processing continues with the next message. Intended to run on a background task in
    /// its own DI scope, not inline with the HTTP request that created the job.
    /// </summary>
    Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken);
}
