using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Sends webhook notifications when DLQ activity exceeds configured thresholds.
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
}
