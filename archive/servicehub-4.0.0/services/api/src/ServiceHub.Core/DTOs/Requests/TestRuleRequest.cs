using System.ComponentModel.DataAnnotations;
using ServiceHub.Core.Models;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Request DTO for testing a rule against active DLQ messages.
/// </summary>
public sealed record TestRuleRequest
{
    /// <summary>
    /// Conditions to test against active DLQ messages.
    /// If empty, uses the conditions from an existing rule (ruleId required).
    /// </summary>
    public IReadOnlyList<RuleCondition>? Conditions { get; init; }

    /// <summary>
    /// Optional existing rule ID to test. Overrides Conditions if both provided.
    /// </summary>
    public long? RuleId { get; init; }

    /// <summary>
    /// Optional namespace filter to limit testing scope.
    /// </summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>
    /// Maximum number of messages to test against (default 100).
    /// </summary>
    [Range(1, 1000)]
    public int MaxMessages { get; init; } = 100;
}
