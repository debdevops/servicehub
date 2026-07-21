using ServiceHub.Core.Enums;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Sends webhook notifications for DLQ activity and bulk operation outcomes. Payload shape
/// (generic JSON, Slack, or Teams) is controlled by <c>WebhookOptions.Format</c> and delegated
/// to an <see cref="IWebhookMessageFormatter"/> — this interface itself never changes to add a
/// new destination format.
/// </summary>
public interface IWebhookNotifier
{
    /// <summary>
    /// Notifies external systems about a DLQ spike in the given namespace.
    /// </summary>
    /// <param name="namespaceId">The namespace that was scanned.</param>
    /// <param name="namespaceName">Human-readable namespace name.</param>
    /// <param name="newMessageCount">Number of new DLQ messages detected in this scan cycle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<Result> NotifyDlqSpikeAsync(
        Guid namespaceId,
        string namespaceName,
        int newMessageCount,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies external systems that a bulk replay/purge job has finished. Unlike the DLQ
    /// spike alert, this is not threshold-gated or cooled-down — every completed job (a
    /// deliberate, human-triggered action) is worth reporting.
    /// </summary>
    /// <param name="jobId">The completed job's identifier.</param>
    /// <param name="operationType">Replay or Purge.</param>
    /// <param name="status">The job's terminal status.</param>
    /// <param name="namespaceId">The namespace the job ran against.</param>
    /// <param name="namespaceName">Human-readable namespace name.</param>
    /// <param name="totalMatched">Total messages the job's filter matched.</param>
    /// <param name="successCount">Messages successfully processed.</param>
    /// <param name="failureCount">Messages that failed.</param>
    /// <param name="skippedCount">Messages skipped without an attempt.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<Result> NotifyBulkOperationCompletedAsync(
        Guid jobId,
        BulkOperationType operationType,
        BulkOperationStatus status,
        Guid namespaceId,
        string namespaceName,
        int totalMatched,
        int successCount,
        int failureCount,
        int skippedCount,
        CancellationToken cancellationToken = default);
}
