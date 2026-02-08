using System.ComponentModel.DataAnnotations;

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
    [Required(ErrorMessage = "Namespace ID is required")]
    Guid NamespaceId,
    
    [Required(ErrorMessage = "Entity name is required")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "Entity name must be between 1 and 256 characters")]
    [RegularExpression(@"^[a-zA-Z0-9][\w\-\.\/]*$", ErrorMessage = "Entity name contains invalid characters")]
    string EntityName,
    
    [StringLength(256, ErrorMessage = "Subscription name cannot exceed 256 characters")]
    [RegularExpression(@"^[a-zA-Z0-9][\w\-\.]*$", ErrorMessage = "Subscription name contains invalid characters")]
    string? SubscriptionName = null,
    
    bool FromDeadLetter = false,
    
    [Range(1, 100, ErrorMessage = "MaxMessages must be between 1 and 100")]
    int MaxMessages = 10,
    
    [Range(0, long.MaxValue, ErrorMessage = "FromSequenceNumber must be non-negative")]
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
