using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Abstraction over a cloud messaging platform (Azure Service Bus, AWS SQS/SNS, GCP Pub/Sub, …).
/// Each provider implementation is discovered at startup and dispatched by <c>CloudProviderRouter</c>.
/// </summary>
public interface ICloudMessagingProvider
{
    /// <summary>
    /// Gets the cloud provider type that this implementation handles.
    /// </summary>
    CloudProviderType ProviderType { get; }

    /// <summary>
    /// Validates whether the credentials in the given namespace are sufficient to
    /// establish a live connection to the remote messaging service.
    /// </summary>
    /// <param name="ns">The namespace whose credentials should be validated.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// <see cref="Result.Success"/> when the connection can be established;
    /// a failure result with a descriptive error otherwise.
    /// </returns>
    Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct);

    /// <summary>
    /// Lists all accessible messaging entities (queues, topics, subscriptions) for the given namespace.
    /// </summary>
    /// <param name="namespaceId">The identifier of the namespace to inspect.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result containing the read-only list of cloud entities on success.</returns>
    Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct);

    /// <summary>
    /// Returns the message receiver bound to this provider's connection infrastructure.
    /// </summary>
    IMessageReceiver GetMessageReceiver();

    /// <summary>
    /// Returns the message sender bound to this provider's connection infrastructure.
    /// </summary>
    IMessageSender GetMessageSender();
}
