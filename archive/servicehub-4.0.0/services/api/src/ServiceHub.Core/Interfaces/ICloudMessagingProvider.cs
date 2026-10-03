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
    /// Gets the set of operations this provider genuinely supports, so callers can ask
    /// "can this provider do X?" once instead of branching on <see cref="ProviderType"/>
    /// at every call site. See <see cref="ProviderCapabilities"/>.
    /// </summary>
    ProviderCapabilities Capabilities { get; }

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
    /// Lists entities the same way <see cref="ListEntitiesAsync"/> does, but also reports which
    /// entities (if any) this scan could not confirm the presence/absence of because a per-entity
    /// discovery call failed. Used by DLQ reconciliation, which must never treat an unconfirmed
    /// entity as deleted. Providers whose listing is atomic (succeeds or fails as a whole — Azure,
    /// GCP, the in-memory mock) never have partial results, so the default implementation simply
    /// wraps <see cref="ListEntitiesAsync"/>; only providers whose discovery can partially fail
    /// per-entity (AWS SQS/SNS) need to override this.
    /// </summary>
    /// <param name="namespaceId">The identifier of the namespace to inspect.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result containing the entity scan result on success.</returns>
    async Task<Result<EntityScanResult>> ListEntitiesForReconciliationAsync(Guid namespaceId, CancellationToken ct)
    {
        var result = await ListEntitiesAsync(namespaceId, ct).ConfigureAwait(false);
        return result.IsSuccess
            ? Result.Success(new EntityScanResult { Entities = result.Value })
            : Result.Failure<EntityScanResult>(result.Error);
    }

    /// <summary>
    /// Returns the message receiver bound to this provider's connection infrastructure.
    /// </summary>
    IMessageReceiver GetMessageReceiver();

    /// <summary>
    /// Returns the message sender bound to this provider's connection infrastructure.
    /// </summary>
    IMessageSender GetMessageSender();
}
