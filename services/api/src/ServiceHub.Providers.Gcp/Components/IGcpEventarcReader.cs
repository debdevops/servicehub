using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Gcp.Components;

/// <summary>An Eventarc trigger, by its full resource name, with the Pub/Sub subscription Eventarc delivers it through.</summary>
/// <param name="Name"><c>projects/p/locations/l/triggers/t</c>.</param>
/// <param name="TransportSubscription">The transport subscription's full name (<c>projects/p/subscriptions/s</c>), or null when the trigger has none.</param>
internal sealed record TriggerRef(string Name, string? TransportSubscription);

/// <summary>One event held in a dead-letter topic: the message's id, when it was published, and its attributes. Never its data.</summary>
internal sealed record UndeliveredEvent(string MessageId, DateTimeOffset? PublishedUtc, IReadOnlyDictionary<string, string> Attributes);

/// <summary>What a dead-letter topic holds, and whether it can be looked at at all.</summary>
/// <param name="Events">The events visible now.</param>
/// <param name="More">Whether more exist than were returned.</param>
/// <param name="Readable">False when no pull subscription is attached to the dead-letter topic, so Pub/Sub keeps nothing that can be read.</param>
internal sealed record DeadLetterRead(IReadOnlyList<UndeliveredEvent> Events, bool More, bool Readable);

/// <summary>
/// The only way <see cref="EventarcSource"/> touches Google: list locations and triggers, get a trigger, read a subscription's
/// dead-letter policy, and look at a dead-letter topic. Narrow on purpose, so a test can stand in for Google.
/// </summary>
internal interface IGcpEventarcReader
{
    /// <summary>The location ids Eventarc is offered in for the connection's project.</summary>
    Task<Listing<string>> ListLocationsAsync(Namespace ns, CancellationToken ct);

    /// <summary>Triggers in one location.</summary>
    Task<Listing<TriggerRef>> ListTriggersAsync(Namespace ns, string location, int max, CancellationToken ct);

    /// <summary>One trigger, or null when Google no longer has it.</summary>
    Task<TriggerRef?> GetTriggerAsync(Namespace ns, string triggerName, CancellationToken ct);

    /// <summary>The dead-letter topic of a subscription (<c>projects/p/topics/t</c>), or null when it has no dead-letter policy or is gone.</summary>
    Task<string?> GetDeadLetterTopicAsync(Namespace ns, string subscriptionName, CancellationToken ct);

    /// <summary>
    /// The events visible in a dead-letter topic, up to <paramref name="max"/>, deleting none. Pub/Sub has no non-destructive read: each
    /// event is pulled and released at once, which counts as a delivery attempt on the subscription that is looked at.
    /// </summary>
    Task<DeadLetterRead> ListUndeliveredAsync(Namespace ns, string deadLetterTopic, int max, CancellationToken ct);

    /// <summary>One event visible now, or null when it is not (gone, or leased by another reader).</summary>
    Task<UndeliveredEvent?> GetUndeliveredAsync(Namespace ns, string deadLetterTopic, string messageId, CancellationToken ct);
}
