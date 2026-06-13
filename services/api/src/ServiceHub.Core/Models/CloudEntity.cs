using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// Represents a single messaging entity (queue, topic, subscription) discovered on a cloud provider.
/// Used by <see cref="Interfaces.ICloudMessagingProvider.ListEntitiesAsync"/> to return a
/// provider-agnostic snapshot of available entities.
/// </summary>
public sealed class CloudEntity
{
    /// <summary>
    /// Gets or sets the entity name (queue name, topic name, or subscription path).
    /// </summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the entity type descriptor (e.g., "Queue", "Topic", "Subscription").
    /// </summary>
    public string EntityType { get; init; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of active (non-dead-lettered) messages currently in the entity.
    /// </summary>
    public long ActiveMessageCount { get; init; }

    /// <summary>
    /// Gets or sets the number of dead-lettered messages currently in the entity.
    /// </summary>
    public long DeadLetterCount { get; init; }

    /// <summary>
    /// Gets or sets the cloud provider that owns this entity.
    /// </summary>
    public CloudProviderType Provider { get; init; }
}
