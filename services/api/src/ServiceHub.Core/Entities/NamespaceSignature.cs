namespace ServiceHub.Core.Entities;

/// <summary>
/// Tracks whether a DLQ error cluster's signature has been seen before in a namespace,
/// so a scan can answer "is this a new problem, or one I already know about?" One row per
/// distinct (namespace, signature hash) pair, owner-scoped.
/// </summary>
public sealed class NamespaceSignature
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>Namespace identifier this signature was observed in.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Owner ID for multi-user isolation. Every query against this table is owner-scoped.</summary>
    public required string OwnerId { get; init; }

    /// <summary>
    /// Stable hash of the cluster's top distinguishing terms plus its dominant deadletter
    /// reason. See <see cref="ServiceHub.Shared.Helpers.ClusterSignatureHasher"/>.
    /// </summary>
    public required string SignatureHash { get; init; }

    /// <summary>When this signature was first observed.</summary>
    public required DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>When this signature was most recently observed.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>Number of scans in which this signature has been observed.</summary>
    public int OccurrenceCount { get; set; }

    /// <summary>Dominant deadletter reason of the cluster as first observed, for display.</summary>
    public required string DominantDeadletterReason { get; init; }

    /// <summary>JSON array of the cluster's top distinguishing terms as first observed, for display.</summary>
    public required string TopTermsJson { get; init; }
}
