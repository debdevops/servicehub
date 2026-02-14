using System.Text.Json.Serialization;

namespace ServiceHub.Core.Models;

/// <summary>
/// Represents a single condition in an auto-replay rule.
/// All conditions in a rule must match for the rule to trigger (AND logic).
/// </summary>
public sealed class RuleCondition
{
    /// <summary>
    /// The message field to evaluate.
    /// </summary>
    /// <remarks>
    /// Supported fields: DeadLetterReason, DeadLetterErrorDescription,
    /// FailureCategory, EntityName, DeliveryCount, ContentType, TopicName,
    /// CorrelationId, ApplicationProperty.
    /// </remarks>
    [JsonPropertyName("field")]
    public required string Field { get; init; }

    /// <summary>
    /// The comparison operator.
    /// </summary>
    /// <remarks>
    /// Supported operators: Contains, NotContains, Equals, NotEquals,
    /// StartsWith, EndsWith, Regex, GreaterThan, LessThan, In.
    /// </remarks>
    [JsonPropertyName("operator")]
    public required string Operator { get; init; }

    /// <summary>
    /// The value to compare against.
    /// </summary>
    [JsonPropertyName("value")]
    public required string Value { get; init; }

    /// <summary>
    /// Whether the comparison is case-sensitive (default: false).
    /// </summary>
    [JsonPropertyName("caseSensitive")]
    public bool CaseSensitive { get; init; }

    /// <summary>
    /// Optional property key when Field is "ApplicationProperty".
    /// </summary>
    [JsonPropertyName("propertyKey")]
    public string? PropertyKey { get; init; }
}
