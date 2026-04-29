using Google.Cloud.PubSub.V1;
using ServiceHub.Core.Entities;

namespace ServiceHub.Infrastructure.Gcp;

/// <summary>
/// Factory interface for creating GCP Pub/Sub publisher and subscriber clients per namespace.
/// Resolved credentials from the namespace's connection string (Service Account JSON) or
/// falls back to Application Default Credentials for Workload Identity environments.
/// </summary>
public interface IGcpClientFactory
{
    /// <summary>
    /// Returns a <see cref="PublisherClient"/> for publishing messages to a topic.
    /// </summary>
    /// <param name="ns">The namespace containing project ID and credentials.</param>
    /// <param name="topicId">The Pub/Sub topic ID (not the full resource name).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A configured <see cref="PublisherClient"/> for the given topic.</returns>
    Task<PublisherClient> GetPublisherClientAsync(Namespace ns, string topicId, CancellationToken ct);

    /// <summary>
    /// Returns a low-level <see cref="SubscriberServiceApiClient"/> for pull-based message
    /// operations (Pull, ModifyAckDeadline, Acknowledge).
    /// </summary>
    /// <param name="ns">The namespace containing project ID and credentials.</param>
    /// <param name="subscriptionId">The Pub/Sub subscription ID (not the full resource name).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A configured <see cref="SubscriberServiceApiClient"/> for the given subscription.</returns>
    Task<SubscriberServiceApiClient> GetSubscriberClientAsync(Namespace ns, string subscriptionId, CancellationToken ct);
}
