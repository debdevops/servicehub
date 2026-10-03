using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// A message ServiceHub found in a dead-letter queue, kept durably so what is stuck is known between
/// visits (unit 2.1). A snapshot taken when the message was first seen, plus a small lifecycle.
/// </summary>
/// <remarks>
/// 4.0.0's shape without its analysis columns (failure category, forensic verdict, replay history,
/// user notes): each of those arrives, by migration, in the unit that produces it.
/// </remarks>
public sealed class DlqMessage
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>The broker's message id.</summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Azure's broker-assigned sequence number. On AWS and GCP it is a stable hash of the message id — it
    /// identifies the message but carries no order, so those providers are matched by <see cref="MessageId"/>.
    /// </summary>
    public required long SequenceNumber { get; init; }

    /// <summary>SHA-256 of the body, or <c>empty</c>.</summary>
    public required string BodyHash { get; init; }

    /// <summary>The connected namespace it was found in.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>The cloud that namespace is on.</summary>
    public required CloudProviderType CloudProvider { get; init; }

    /// <summary>The owner of that namespace.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The queue, or <c>topic/subscriptions/subscription</c>. One format for every cloud.</summary>
    public required string EntityName { get; init; }

    /// <summary>Queue or subscription.</summary>
    public required ServiceBusEntityType EntityType { get; init; }

    /// <summary>The topic, when the entity is a subscription.</summary>
    public string? TopicName { get; init; }

    /// <summary>When the message was first enqueued.</summary>
    public required DateTimeOffset EnqueuedTimeUtc { get; init; }

    /// <summary>
    /// When ServiceHub first saw it dead-lettered. No provider's peek exposes the real dead-letter time,
    /// so this is the truthful proxy — it is not approximated with the enqueue time.
    /// </summary>
    public required DateTimeOffset DetectedAtUtc { get; init; }

    /// <summary>The reason the broker or application gave.</summary>
    public string? DeadLetterReason { get; init; }

    /// <summary>The error description that came with it.</summary>
    public string? DeadLetterErrorDescription { get; init; }

    /// <summary>How many times delivery was attempted.</summary>
    public int DeliveryCount { get; init; }

    /// <summary>Body content type.</summary>
    public string? ContentType { get; init; }

    /// <summary>Size in bytes.</summary>
    public long MessageSize { get; init; }

    /// <summary>The first 500 characters of the body.</summary>
    public string? BodyPreview { get; init; }

    /// <summary>The application properties, as JSON.</summary>
    public string? ApplicationPropertiesJson { get; init; }

    /// <summary>The message's correlation id.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>The message's session id.</summary>
    public string? SessionId { get; init; }

    /// <summary>The failure fingerprint hash assigned when the message was first seen (unit 3.1). Null for rows recorded before it.</summary>
    public string? SignatureHash { get; set; }

    /// <summary>
    /// Where it is now. A concurrency token: two writers racing on one row (a scan and a replay) cannot
    /// both win — the loser gets <c>DbUpdateConcurrencyException</c> rather than silently overwriting.
    /// </summary>
    public DlqMessageStatus Status { get; set; } = DlqMessageStatus.Active;

    /// <summary>When a scan found it gone from the queue.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>What is known about why it is gone. Null unless <see cref="Status"/> is Resolved.</summary>
    public DlqResolutionCause? ResolutionCause { get; set; }

    /// <summary>When it was archived because its namespace was removed.</summary>
    public DateTimeOffset? ArchivedAt { get; set; }
}
