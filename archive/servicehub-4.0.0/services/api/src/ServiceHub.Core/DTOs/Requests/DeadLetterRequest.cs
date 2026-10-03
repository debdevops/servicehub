namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Request to dead-letter messages from a queue or subscription.
/// This is primarily used for testing DLQ functionality.
/// </summary>
/// <param name="NamespaceId">The namespace identifier.</param>
/// <param name="EntityName">The queue or topic name.</param>
/// <param name="SubscriptionName">Optional subscription name for topics.</param>
/// <param name="MessageCount">Number of messages to dead-letter (default 1, max 10 for safety).</param>
/// <param name="Reason">The dead-letter reason to set on the messages.</param>
/// <param name="ErrorDescription">Optional error description to set on the messages.</param>
public sealed record DeadLetterRequest(
    Guid NamespaceId,
    string EntityName,
    string? SubscriptionName,
    int MessageCount = 1,
    string Reason = "ManualDeadLetter",
    string? ErrorDescription = null)
{
    /// <summary>
    /// Maximum number of messages that can be dead-lettered in a single request.
    /// </summary>
    public const int MaxMessageCount = 10;

    /// <summary>
    /// Validates the message count is within allowed bounds.
    /// </summary>
    public int ValidatedMessageCount => Math.Clamp(MessageCount, 1, MaxMessageCount);
}
