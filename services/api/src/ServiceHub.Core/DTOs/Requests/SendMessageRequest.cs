namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Request DTO for sending a message to a queue or topic.
/// </summary>
/// <param name="NamespaceId">The ID of the namespace to send the message to.</param>
/// <param name="EntityName">The name of the queue or topic.</param>
/// <param name="Body">The message body content.</param>
/// <param name="ContentType">Optional content type of the message body (e.g., application/json).</param>
/// <param name="CorrelationId">Optional correlation ID for request-reply patterns.</param>
/// <param name="SessionId">Optional session ID for session-enabled entities.</param>
/// <param name="PartitionKey">Optional partition key for partitioned entities.</param>
/// <param name="Subject">Optional subject/label for the message.</param>
/// <param name="ReplyTo">Optional reply-to address for request-reply patterns.</param>
/// <param name="ReplyToSessionId">Optional reply-to session ID.</param>
/// <param name="To">Optional address for forwarding.</param>
/// <param name="TimeToLiveSeconds">Optional time-to-live in seconds.</param>
/// <param name="ScheduledEnqueueTimeUtc">Optional scheduled enqueue time for delayed delivery.</param>
/// <param name="ApplicationProperties">Optional application-specific properties.</param>
public sealed record SendMessageRequest(
    Guid NamespaceId,
    string EntityName,
    string Body,
    string? ContentType = null,
    string? CorrelationId = null,
    string? SessionId = null,
    string? PartitionKey = null,
    string? Subject = null,
    string? ReplyTo = null,
    string? ReplyToSessionId = null,
    string? To = null,
    int? TimeToLiveSeconds = null,
    DateTimeOffset? ScheduledEnqueueTimeUtc = null,
    IReadOnlyDictionary<string, object>? ApplicationProperties = null);
