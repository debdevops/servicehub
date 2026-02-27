namespace ServiceHub.Core.Models;

/// <summary>
/// Result of evaluating an auto-replay rule against a DLQ message.
/// </summary>
public sealed class RuleMatchResult
{
    /// <summary>
    /// The ID of the DLQ message evaluated.
    /// </summary>
    public required long MessageId { get; init; }

    /// <summary>
    /// The message ID string from Service Bus.
    /// </summary>
    public required string ServiceBusMessageId { get; init; }

    /// <summary>
    /// The entity name (queue or subscription).
    /// </summary>
    public required string EntityName { get; init; }

    /// <summary>
    /// Whether the rule matched this message.
    /// </summary>
    public required bool IsMatch { get; init; }

    /// <summary>
    /// Description of the match result (why it matched or didn't).
    /// </summary>
    public string? MatchReason { get; init; }

    /// <summary>
    /// The dead-letter reason for context.
    /// </summary>
    public string? DeadLetterReason { get; init; }
}
