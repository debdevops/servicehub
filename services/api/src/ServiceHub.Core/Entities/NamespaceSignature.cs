namespace ServiceHub.Core.Entities;

/// <summary>
/// One distinct failure fingerprint seen in a namespace: what rules, the gate and (later) autonomy reason about
/// (unit 3.1). One row per (owner, namespace, signature hash). Two messages that fail the same way share a row;
/// two that fail differently do not.
/// </summary>
/// <remarks>
/// Copied in shape from 4.0.0 without <c>HashKind</c>: 4.0.0 kept a second "cluster" vocabulary beside the trust
/// fingerprint, and 4.1.0 has only the fingerprint. The hash is the fingerprint's <c>Hash</c>, unchanged — changing
/// how it is computed silently invalidates every trust score keyed by it.
/// </remarks>
public sealed class NamespaceSignature
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>The namespace this signature was seen in.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Owner scope. Every query against this table is owner-scoped.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The fingerprint's stable hash.</summary>
    public required string SignatureHash { get; init; }

    /// <summary>When this signature was first seen.</summary>
    public required DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>When it was last seen.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>How many messages have carried it.</summary>
    public int OccurrenceCount { get; set; }

    /// <summary>The reason the cloud recorded, as first seen, for display.</summary>
    public required string DominantDeadletterReason { get; init; }

    /// <summary>The queue it was first seen in, for display.</summary>
    public required string EntityName { get; init; }

    /// <summary>An example error text as first seen (may be empty), for display. Plain words lead; the hash is secondary.</summary>
    public string? ExampleError { get; init; }

    /// <summary>JSON array of the fingerprint's distinguishing terms, for display.</summary>
    public required string TopTermsJson { get; init; }
}
