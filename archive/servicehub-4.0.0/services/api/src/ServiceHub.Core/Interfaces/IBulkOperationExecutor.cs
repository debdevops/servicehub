namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Executes a single, already-persisted <see cref="Entities.BulkOperationJob"/>: loads the
/// matched messages, replays/purges each one via <see cref="IMessageOperationsService"/>,
/// updates progress as it goes, and records the terminal outcome. Split out from
/// <see cref="IBulkOperationService"/> (which is request-scoped, HTTP-facing) so the background
/// worker can resolve and unit-test the execution path independently of the API layer.
/// </summary>
public interface IBulkOperationExecutor
{
    /// <summary>
    /// Runs the job identified by <paramref name="jobId"/> to completion, cancellation, or
    /// failure. Never throws for expected per-message failures — those are recorded on the job
    /// and processing continues with the next message.
    /// </summary>
    Task ExecuteAsync(Guid jobId, CancellationToken cancellationToken);
}
