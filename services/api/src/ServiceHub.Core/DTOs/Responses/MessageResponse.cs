using ServiceHub.Core.Enums;

namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Response DTO for message information.
/// </summary>
/// <param name="MessageId">The unique message identifier.</param>
/// <param name="SequenceNumber">The sequence number assigned by Service Bus.</param>
/// <param name="Body">The message body content.</param>
/// <param name="ContentType">The content type of the message body.</param>
/// <param name="CorrelationId">The correlation identifier.</param>
/// <param name="SessionId">The session identifier.</param>
/// <param name="PartitionKey">The partition key.</param>
/// <param name="Subject">The message subject/label.</param>
/// <param name="ReplyTo">The reply-to address.</param>
/// <param name="ReplyToSessionId">The reply-to session identifier.</param>
/// <param name="To">The destination address.</param>
/// <param name="TimeToLive">The time-to-live duration.</param>
/// <param name="ScheduledEnqueueTime">The scheduled enqueue time.</param>
/// <param name="EnqueuedTime">When the message was enqueued.</param>
/// <param name="ExpiresAt">When the message expires.</param>
/// <param name="LockedUntil">When the message lock expires.</param>
/// <param name="DeliveryCount">The number of delivery attempts.</param>
/// <param name="State">The current state of the message.</param>
/// <param name="DeadLetterSource">The dead-letter source if applicable.</param>
/// <param name="DeadLetterReason">The dead-letter reason if applicable.</param>
/// <param name="DeadLetterErrorDescription">The dead-letter error description if applicable.</param>
/// <param name="ApplicationProperties">Application-specific properties.</param>
/// <param name="SizeInBytes">The size of the message in bytes.</param>
/// <param name="EntityName">The name of the queue or topic.</param>
/// <param name="SubscriptionName">The subscription name if from a topic.</param>
/// <param name="IsFromDeadLetter">Whether the message is from a dead-letter queue.</param>
public sealed record MessageResponse(
    string MessageId,
    long SequenceNumber,
    string? Body,
    string? ContentType,
    string? CorrelationId,
    string? SessionId,
    string? PartitionKey,
    string? Subject,
    string? ReplyTo,
    string? ReplyToSessionId,
    string? To,
    TimeSpan? TimeToLive,
    DateTimeOffset? ScheduledEnqueueTime,
    DateTimeOffset EnqueuedTime,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? LockedUntil,
    int DeliveryCount,
    MessageState State,
    string? DeadLetterSource,
    string? DeadLetterReason,
    string? DeadLetterErrorDescription,
    IReadOnlyDictionary<string, object>? ApplicationProperties,
    long SizeInBytes,
    string? EntityName,
    string? SubscriptionName,
    bool IsFromDeadLetter);
