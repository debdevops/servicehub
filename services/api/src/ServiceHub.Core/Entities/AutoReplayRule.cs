namespace ServiceHub.Core.Entities;

/// <summary>
/// Defines an automated replay rule for dead-letter queue messages.
/// When enabled, the DLQ monitor will automatically replay matching messages
/// according to the configured conditions and actions.
/// </summary>
public sealed class AutoReplayRule
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>Human-readable name of the rule.</summary>
    public required string Name { get; init; }

    /// <summary>Description of what the rule does.</summary>
    public string? Description { get; init; }

    /// <summary>Whether the rule is currently active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>JSON-serialized conditions that must match for the rule to trigger.</summary>
    public required string ConditionsJson { get; init; }

    /// <summary>JSON-serialized actions to execute when the rule triggers.</summary>
    public required string ActionsJson { get; init; }

    /// <summary>When the rule was created.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the rule was last modified.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Total number of messages matched by this rule.</summary>
    public long MatchCount { get; set; }

    /// <summary>Number of successful replays triggered by this rule.</summary>
    public long SuccessCount { get; set; }

    /// <summary>Maximum number of replays per hour (rate limiting).</summary>
    public int MaxReplaysPerHour { get; init; } = 100;

    /// <summary>Navigation property: replay history entries triggered by this rule.</summary>
    public ICollection<ReplayHistory> ReplayHistories { get; init; } = new List<ReplayHistory>();
}
