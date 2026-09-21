namespace ServiceHub.Core.Models;

/// <summary>
/// Result of a reconciliation-safe entity discovery scan: the entities the provider could
/// confirm, plus which entities' presence/absence could NOT be confirmed because a per-entity
/// discovery call failed (e.g. AWS <c>GetQueueAttributes</c>/SNS <c>ListSubscriptionsByTopic</c>
/// failing for one queue/topic while the rest of the scan succeeds). Reconciliation logic
/// (<see cref="Interfaces.ICloudMessagingProvider.ListEntitiesForReconciliationAsync"/>) must
/// never treat an entity named here as deleted — only that this scan could not confirm it either
/// way.
/// </summary>
public sealed class EntityScanResult
{
    /// <summary>
    /// Gets the entities this scan successfully confirmed — identical in shape to what
    /// <see cref="Interfaces.ICloudMessagingProvider.ListEntitiesAsync"/> returns.
    /// </summary>
    public required IReadOnlyList<CloudEntity> Entities { get; init; }

    /// <summary>
    /// Gets the names of queues whose attributes/message-count listing failed this scan.
    /// </summary>
    public IReadOnlySet<string> IncompleteQueueNames { get; init; } = new HashSet<string>();

    /// <summary>
    /// Gets the names of topics whose subscription listing failed this scan — that topic's
    /// subscriptions are unverified, even though the topic itself was listed successfully.
    /// </summary>
    public IReadOnlySet<string> IncompleteTopicNames { get; init; } = new HashSet<string>();

    /// <summary>
    /// Gets a value indicating whether listing topics/subscriptions failed entirely this scan,
    /// meaning no topic names are known at all and every subscription is unverified.
    /// </summary>
    public bool SnsListingFailed { get; init; }
}
