using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// Represents a message from Azure Service Bus.
/// This is a pure data model representing message data without behavior.
/// </summary>
public sealed class Message
{
    /// <summary>
    /// Gets or sets the unique identifier of the message.
    /// </summary>
    public required string MessageId { get; init; }

    /// <summary>
    /// Gets or sets the sequence number assigned by Service Bus.
    /// </summary>
    public long SequenceNumber { get; init; }

    /// <summary>
    /// Gets or sets the body of the message as a string.
    /// </summary>
    public string? Body { get; init; }

    /// <summary>
    /// Gets or sets the content type of the message body.
    /// </summary>
    public string? ContentType { get; init; }

    /// <summary>
    /// Gets or sets the correlation identifier for request-reply patterns.
    /// </summary>
    public string? CorrelationId { get; init; }

    /// <summary>
    /// Gets or sets the session identifier for session-enabled entities.
    /// </summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Gets or sets the partition key for partitioned entities.
    /// </summary>
    public string? PartitionKey { get; init; }

    /// <summary>
    /// Gets or sets the reply-to address for request-reply patterns.
    /// </summary>
    public string? ReplyTo { get; init; }

    /// <summary>
    /// Gets or sets the reply-to session identifier.
    /// </summary>
    public string? ReplyToSessionId { get; init; }

    /// <summary>
    /// Gets or sets the address for forwarding the message.
    /// </summary>
    public string? To { get; init; }

    /// <summary>
    /// Gets or sets the subject/label of the message.
    /// </summary>
    public string? Subject { get; init; }

    /// <summary>
    /// Gets or sets the time-to-live duration for the message.
    /// </summary>
    public TimeSpan? TimeToLive { get; init; }

    /// <summary>
    /// Gets or sets the scheduled enqueue time for delayed messages.
    /// </summary>
    public DateTimeOffset? ScheduledEnqueueTime { get; init; }

    /// <summary>
    /// Gets or sets the time when the message was enqueued.
    /// </summary>
    public DateTimeOffset EnqueuedTime { get; init; }

    /// <summary>
    /// Gets or sets the time when the message expires.
    /// </summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>
    /// Gets or sets the time when the message's lock expires.
    /// </summary>
    public DateTimeOffset? LockedUntil { get; init; }

    /// <summary>
    /// Gets or sets the lock token for the message.
    /// </summary>
    public string? LockToken { get; init; }

    /// <summary>
    /// Gets or sets the delivery count of the message.
    /// </summary>
    public int DeliveryCount { get; init; }

    /// <summary>
    /// Gets or sets the current state of the message.
    /// </summary>
    public MessageState State { get; init; }

    /// <summary>
    /// Gets or sets the dead-letter source if the message is dead-lettered.
    /// </summary>
    public string? DeadLetterSource { get; init; }

    /// <summary>
    /// Gets or sets the dead-letter reason.
    /// </summary>
    public string? DeadLetterReason { get; init; }

    /// <summary>
    /// Gets or sets the dead-letter error description.
    /// </summary>
    public string? DeadLetterErrorDescription { get; init; }

    /// <summary>
    /// Gets or sets the application-specific properties.
    /// </summary>
    public IReadOnlyDictionary<string, object>? ApplicationProperties { get; init; }

    /// <summary>
    /// Gets or sets the size of the message in bytes.
    /// </summary>
    public long SizeInBytes { get; init; }

    /// <summary>
    /// Gets or sets the namespace ID this message belongs to.
    /// </summary>
    public Guid NamespaceId { get; init; }

    /// <summary>
    /// Gets or sets the name of the queue or topic this message is from.
    /// </summary>
    public string? EntityName { get; init; }

    /// <summary>
    /// Gets or sets the subscription name if this message is from a topic subscription.
    /// </summary>
    public string? SubscriptionName { get; init; }

    /// <summary>
    /// Gets or sets whether this message is from the dead-letter queue.
    /// </summary>
    public bool IsFromDeadLetter { get; init; }

    /// <summary>
    /// Gets or sets the enqueued sequence number for dead-letter messages.
    /// </summary>
    public long? EnqueuedSequenceNumber { get; init; }
}
