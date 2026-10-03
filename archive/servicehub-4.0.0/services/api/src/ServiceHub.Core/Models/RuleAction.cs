using System.Text.Json.Serialization;

namespace ServiceHub.Core.Models;

/// <summary>
/// Represents the action to execute when an auto-replay rule matches.
/// </summary>
public sealed class RuleAction
{
    /// <summary>
    /// Whether to auto-replay matched messages.
    /// </summary>
    [JsonPropertyName("autoReplay")]
    public bool AutoReplay { get; init; } = true;

    /// <summary>
    /// Delay in seconds before replaying the message.
    /// </summary>
    [JsonPropertyName("delaySeconds")]
    public int DelaySeconds { get; init; } = 60;

    /// <summary>
    /// Maximum number of retry attempts for failed replays.
    /// </summary>
    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Whether to use exponential backoff for retries.
    /// </summary>
    [JsonPropertyName("exponentialBackoff")]
    public bool ExponentialBackoff { get; init; }

    /// <summary>
    /// Optional alternate entity to replay to (instead of original).
    /// </summary>
    [JsonPropertyName("targetEntity")]
    public string? TargetEntity { get; init; }
}
