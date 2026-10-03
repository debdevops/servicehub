namespace ServiceHub.Core.Entities;

/// <summary>
/// Records each replay attempt for a dead-letter queue message.
/// Links to the parent <see cref="DlqMessage"/> and optionally to an <see cref="AutoReplayRule"/>.
/// </summary>
public sealed class ReplayHistory
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>Foreign key to the DLQ message that was replayed.</summary>
    public required long DlqMessageId { get; init; }

    /// <summary>Optional foreign key to the auto-replay rule that triggered the replay.</summary>
    public long? RuleId { get; init; }

    /// <summary>When the replay was executed.</summary>
    public required DateTimeOffset ReplayedAt { get; init; }

    /// <summary>Who or what initiated the replay (user email, "system", "auto-rule", etc.).</summary>
    public required string ReplayedBy { get; init; }

    /// <summary>The strategy used for replay (e.g., "original-entity", "alternate-entity", "modified").</summary>
    public required string ReplayStrategy { get; init; }

    /// <summary>The target entity the message was replayed to.</summary>
    public required string ReplayedToEntity { get; init; }

    /// <summary>Outcome of the replay attempt.</summary>
    public required string OutcomeStatus { get; init; }

    /// <summary>If the replayed message was dead-lettered again, the new dead-letter reason.</summary>
    public string? NewDeadLetterReason { get; init; }

    /// <summary>Optional error details from a failed replay.</summary>
    public string? ErrorDetails { get; init; }

    /// <summary>Navigation property: the parent DLQ message.</summary>
    public DlqMessage? DlqMessage { get; init; }

    /// <summary>Navigation property: the auto-replay rule (if applicable).</summary>
    public AutoReplayRule? Rule { get; init; }
}
