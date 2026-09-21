namespace ServiceHub.Core.Entities;

/// <summary>
/// A point-in-time snapshot of a <see cref="FailureKnowledgeEntity"/> row, captured immediately
/// before it is overwritten by an update. Preserves version history so prior root cause,
/// resolution notes, and other fields remain retrievable after an edit.
/// </summary>
public sealed class FailureKnowledgeHistoryEntity
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>Namespace identifier this knowledge belongs to.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Owner ID for multi-tenant isolation.</summary>
    public required string OwnerId { get; init; }

    /// <summary>Signature hash this knowledge is linked to.</summary>
    public required string SignatureHash { get; init; }

    /// <summary>The version number this snapshot captured (the value before it was incremented).</summary>
    public required int KnowledgeVersion { get; init; }

    /// <summary>Root cause analysis, as it stood at this version.</summary>
    public string? RootCause { get; init; }

    /// <summary>Resolution notes, as they stood at this version.</summary>
    public string? ResolutionNotes { get; init; }

    /// <summary>Operational context, as it stood at this version.</summary>
    public string? OperationalNotes { get; init; }

    /// <summary>Link to runbook, as it stood at this version.</summary>
    public string? RunbookLink { get; init; }

    /// <summary>Owner email/identifier, as it stood at this version.</summary>
    public string? Owner { get; init; }

    /// <summary>Replay guidance, as it stood at this version.</summary>
    public string? ReplayGuidance { get; init; }

    /// <summary>Comma-separated tags, as they stood at this version.</summary>
    public string? Tags { get; init; }

    /// <summary>Review due date, as it stood at this version.</summary>
    public DateTimeOffset? ReviewDueAt { get; init; }

    /// <summary>Free-text identifier of who made this version's edit, if supplied.</summary>
    public string? UpdatedBy { get; init; }

    /// <summary>When this version was superseded (i.e. when the snapshot was taken).</summary>
    public required DateTimeOffset UpdatedAt { get; init; }
}
