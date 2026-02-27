using System.ComponentModel.DataAnnotations;
using ServiceHub.Core.Models;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Request DTO for creating or updating an auto-replay rule.
/// </summary>
public sealed record CreateRuleRequest
{
    /// <summary>Human-readable name for the rule.</summary>
    [Required]
    [StringLength(256, MinimumLength = 1)]
    public required string Name { get; init; }

    /// <summary>Description of what the rule does.</summary>
    [StringLength(1024)]
    public string? Description { get; init; }

    /// <summary>Whether the rule is enabled.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Conditions that must all match (AND logic).</summary>
    [Required]
    [MinLength(1)]
    public required IReadOnlyList<RuleCondition> Conditions { get; init; }

    /// <summary>Action to execute when all conditions match.</summary>
    [Required]
    public required RuleAction Action { get; init; }

    /// <summary>Maximum replays per hour (rate limiting). Default 100.</summary>
    [Range(1, 10000)]
    public int MaxReplaysPerHour { get; init; } = 100;
}
