namespace ServiceHub.Core.Entities;

/// <summary>
/// Persistent storage for operational knowledge about a FailureSignature.
/// Linked to NamespaceSignature by (NamespaceId, SignatureHash) pair.
/// </summary>
public sealed class FailureKnowledgeEntity
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>Namespace identifier this knowledge belongs to.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Owner ID for multi-tenant isolation.</summary>
    public required string OwnerId { get; init; }

    /// <summary>
    /// Signature hash this knowledge is linked to.
    /// Same as NamespaceSignature.SignatureHash for correlation.
    /// </summary>
    public required string SignatureHash { get; init; }

    /// <summary>Root cause analysis.</summary>
    public string? RootCause { get; set; }

    /// <summary>Resolution notes.</summary>
    public string? ResolutionNotes { get; set; }

    /// <summary>Operational context.</summary>
    public string? OperationalNotes { get; set; }

    /// <summary>Link to runbook.</summary>
    public string? RunbookLink { get; set; }

    /// <summary>Owner email/identifier.</summary>
    public string? Owner { get; set; }

    /// <summary>Replay guidance (safe/unsafe/investigate).</summary>
    public string? ReplayGuidance { get; set; }

    /// <summary>When this knowledge was last updated.</summary>
    public DateTimeOffset? LastUpdatedAt { get; set; }

    /// <summary>Version of this knowledge.</summary>
    public int KnowledgeVersion { get; set; } = 1;

    /// <summary>When to review this knowledge next.</summary>
    public DateTimeOffset? ReviewDueAt { get; set; }

    /// <summary>Semicolon-separated tags.</summary>
    public string? Tags { get; set; }

    /// <summary>Created timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
