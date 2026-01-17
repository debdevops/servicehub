namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Request DTO for retrieving messages from a queue, subscription, or dead-letter queue.
/// </summary>
/// <param name="NamespaceId">The ID of the namespace.</param>
/// <param name="EntityName">The name of the queue or topic.</param>
/// <param name="SubscriptionName">Optional subscription name for topic subscriptions.</param>
/// <param name="FromDeadLetter">Whether to retrieve from the dead-letter queue.</param>
/// <param name="MaxMessages">Maximum number of messages to retrieve (1-100).</param>
/// <param name="FromSequenceNumber">Optional sequence number to start reading from.</param>
public sealed record GetMessagesRequest(
    Guid NamespaceId,
    string EntityName,
    string? SubscriptionName = null,
    bool FromDeadLetter = false,
    int MaxMessages = 10,
    long? FromSequenceNumber = null)
{
    /// <summary>
    /// Maximum allowed value for MaxMessages.
    /// </summary>
    public const int MaxAllowedMessages = 100;

    /// <summary>
    /// Minimum allowed value for MaxMessages.
    /// </summary>
    public const int MinAllowedMessages = 1;
}
