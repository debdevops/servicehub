using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// One durable transition event in a failure signature's lifecycle history. Append-only —
/// rows are never updated or deleted. Linked to <see cref="SignatureLifecycleState"/> only by
/// natural key (<see cref="OwnerId"/>, <see cref="NamespaceId"/>, <see cref="SignatureHash"/>),
/// no foreign key — the same no-FK convention <see cref="FailureKnowledgeHistoryEntity"/> uses.
/// </summary>
public sealed class SignatureLifecycleHistoryEntry
{
    /// <summary>Primary key (autoincrement).</summary>
    public long Id { get; init; }

    /// <summary>Owner ID for multi-user isolation. Same format as <see cref="DlqMessage.OwnerId"/>.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The namespace the signature belongs to.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>The signature's stable identity hash.</summary>
    public required string SignatureHash { get; init; }

    /// <summary>The status the signature transitioned from.</summary>
    public required SignatureLifecycleStatus FromStatus { get; init; }

    /// <summary>The status the signature transitioned to.</summary>
    public required SignatureLifecycleStatus ToStatus { get; init; }

    /// <summary>When the transition occurred.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Free-text notes captured with this transition.</summary>
    public string? Notes { get; init; }
}
