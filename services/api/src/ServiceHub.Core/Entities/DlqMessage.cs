using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// Represents a message detected in a dead-letter queue.
/// Stores the complete snapshot of the message at detection time
/// along with categorization and lifecycle tracking data.
/// </summary>
public sealed class DlqMessage
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>The Service Bus message ID.</summary>
    public required string MessageId { get; init; }

    /// <summary>The Service Bus sequence number in the DLQ.</summary>
    public required long SequenceNumber { get; init; }

    /// <summary>SHA-256 hash of the message body for deduplication.</summary>
    public required string BodyHash { get; init; }

    /// <summary>Namespace identifier (matches the registered namespace ID).</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>The queue or subscription name.</summary>
    public required string EntityName { get; init; }

    /// <summary>Type of the entity (queue or subscription).</summary>
    public required ServiceBusEntityType EntityType { get; init; }

    /// <summary>When the message was originally enqueued.</summary>
    public required DateTimeOffset EnqueuedTimeUtc { get; init; }

    /// <summary>When the message was moved to the dead-letter queue.</summary>
    public DateTimeOffset? DeadLetterTimeUtc { get; init; }

    /// <summary>When the DLQ monitor first detected this message.</summary>
    public required DateTimeOffset DetectedAtUtc { get; init; }

    /// <summary>The dead-letter reason assigned by Service Bus or the application.</summary>
    public string? DeadLetterReason { get; init; }

    /// <summary>The dead-letter error description.</summary>
    public string? DeadLetterErrorDescription { get; init; }

    /// <summary>Number of delivery attempts before dead-lettering.</summary>
    public int DeliveryCount { get; init; }

    /// <summary>The content type of the message body.</summary>
    public string? ContentType { get; init; }

    /// <summary>Size of the message in bytes.</summary>
    public long MessageSize { get; init; }

    /// <summary>Preview of the message body (first 500 characters).</summary>
    public string? BodyPreview { get; init; }

    /// <summary>JSON-serialized application properties.</summary>
    public string? ApplicationPropertiesJson { get; init; }

    /// <summary>Heuristic failure category.</summary>
    public FailureCategory FailureCategory { get; set; } = FailureCategory.Unknown;

    /// <summary>Confidence score of the failure categorization (0.0–1.0).</summary>
    public double CategoryConfidence { get; set; }

    /// <summary>Current lifecycle status of this DLQ message.</summary>
    public DlqMessageStatus Status { get; set; } = DlqMessageStatus.Active;

    /// <summary>When the message was replayed (if applicable).</summary>
    public DateTimeOffset? ReplayedAt { get; set; }

    /// <summary>Whether the replay was successful.</summary>
    public bool? ReplaySuccess { get; set; }

    /// <summary>When the message was archived (if applicable).</summary>
    public DateTimeOffset? ArchivedAt { get; set; }

    /// <summary>When the message was detected as no longer present in the DLQ.</summary>
    public DateTimeOffset? ResolvedAt { get; set; }

    /// <summary>User-added notes for investigation.</summary>
    public string? UserNotes { get; set; }

    /// <summary>Correlation ID from the original message.</summary>
    public string? CorrelationId { get; init; }

    /// <summary>Session ID from the original message.</summary>
    public string? SessionId { get; init; }

    /// <summary>Topic name when the entity is a subscription.</summary>
    public string? TopicName { get; init; }

    /// <summary>Navigation property: replay history entries.</summary>
    public ICollection<ReplayHistory> ReplayHistories { get; init; } = new List<ReplayHistory>();
}
