namespace ServiceHub.Core.Entities;

/// <summary>
/// One replay attempt on one dead letter — what the Replayed tab lists (unit 2.7). The ledger entry it
/// points at holds the evidence and, later, the outcome; this row is the readable index over it.
/// </summary>
/// <remarks>
/// 4.0.0's shape, minus its auto-replay-rule navigation and its cascade foreign key to the message: a
/// history of what was done must outlive the row it was done to, exactly as the ledger does. Three
/// columns are new for 4.1.0 — <see cref="OwnerId"/> and <see cref="NamespaceId"/> (so the list can be
/// scoped without joining a row that may be gone) and <see cref="RecoveryEntryId"/> (the ledger entry).
/// </remarks>
public sealed class ReplayHistory
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>The dead letter that was replayed. A soft reference — no foreign key.</summary>
    public required long DlqMessageId { get; init; }

    /// <summary>The auto-replay rule that triggered it, if one did. No foreign key.</summary>
    public long? RuleId { get; init; }

    /// <summary>Owner of the namespace.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The namespace it happened in.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>The ledger entry that carries the evidence. Null only for rows that predate it.</summary>
    public Guid? RecoveryEntryId { get; init; }

    /// <summary>The broker's id of the replayed message, for reading the row without the message.</summary>
    public required string MessageId { get; init; }

    /// <summary>The dead-letter queue it was replayed from (the queue or subscription name).</summary>
    public required string SourceEntity { get; init; }

    /// <summary>When the replay was executed.</summary>
    public required DateTimeOffset ReplayedAt { get; init; }

    /// <summary>Who or what initiated it — the actor identity as the ledger recorded it.</summary>
    public required string ReplayedBy { get; init; }

    /// <summary>The strategy used (<c>original-entity</c> today).</summary>
    public required string ReplayStrategy { get; init; }

    /// <summary>The entity the message was sent back to.</summary>
    public required string ReplayedToEntity { get; init; }

    /// <summary>What the provider said: <c>accepted</c>, <c>rejected</c> or <c>unknown</c>.</summary>
    public required string OutcomeStatus { get; init; }

    /// <summary>If it was dead-lettered again, the new reason. Filled by the verification unit.</summary>
    public string? NewDeadLetterReason { get; init; }

    /// <summary>Error details from a failed replay.</summary>
    public string? ErrorDetails { get; init; }
}
