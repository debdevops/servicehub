using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Provides GCP Pub/Sub-specific ack-deadline and dead-letter policy status.
/// Implemented by both <c>GcpMessageReceiver</c> and <c>SimulatedGcpReceiver</c>.
/// </summary>
public interface IAckDeadlineStatusProvider
{
    /// <summary>
    /// Returns the ack-deadline and dead-letter policy configuration for the given GCP subscription.
    /// </summary>
    /// <param name="namespaceId">The GCP project/namespace identifier.</param>
    /// <param name="subscriptionId">The Pub/Sub subscription name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<GcpAckDeadlineStatus>> GetAckDeadlineStatusAsync(
        Guid namespaceId, string subscriptionId, CancellationToken cancellationToken = default);
}
