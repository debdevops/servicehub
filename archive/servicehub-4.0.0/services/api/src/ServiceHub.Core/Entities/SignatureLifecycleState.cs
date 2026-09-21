using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// Durable current lifecycle status (Active/Resolved/Reopened/Suppressed/Archived) of one
/// failure signature, keyed by the natural key (<see cref="OwnerId"/>, <see cref="NamespaceId"/>,
/// <see cref="SignatureHash"/>). A signature with no row here is implicitly <c>Active</c> — the
/// same default the prior in-memory implementation used, preserved so no backfill is needed.
/// </summary>
public sealed class SignatureLifecycleState
{
    /// <summary>Primary key (autoincrement).</summary>
    public long Id { get; init; }

    /// <summary>Owner ID for multi-user isolation. Same format as <see cref="DlqMessage.OwnerId"/>.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The namespace the signature belongs to.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>The signature's stable identity hash.</summary>
    public required string SignatureHash { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public required SignatureLifecycleStatus Status { get; set; }

    /// <summary>The status this signature transitioned from to reach <see cref="Status"/>. Null until the first transition.</summary>
    public SignatureLifecycleStatus? PreviousStatus { get; set; }

    /// <summary>When the current status was reached. Null until the first transition.</summary>
    public DateTimeOffset? TransitionedAt { get; set; }

    /// <summary>Free-text notes captured with the most recent transition.</summary>
    public string? Notes { get; set; }

    /// <summary>When this row was first created (the signature's first transition).</summary>
    public required DateTimeOffset CreatedAt { get; init; }
}
