namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Processes one signature-replay job's messages sequentially through
/// <see cref="IMessageOperationsService"/>, updating the job's in-memory progress
/// (<see cref="ISignatureReplayJobStore"/>) as it goes. Mirrors <see cref="IBulkOperationExecutor"/>'s
/// role, minus the persisted-job load/save — progress lives entirely on the
/// <see cref="SignatureReplayJobState"/> instance passed in.
/// </summary>
public interface ISignatureReplayExecutor
{
    /// <summary>
    /// Runs the job to completion (or until cancelled/faulted), updating <paramref name="job"/>
    /// in place. Intended to run on a background task in its own DI scope, not inline with the
    /// HTTP request that created the job.
    /// </summary>
    Task ExecuteAsync(SignatureReplayJobState job, CancellationToken cancellationToken);
}
