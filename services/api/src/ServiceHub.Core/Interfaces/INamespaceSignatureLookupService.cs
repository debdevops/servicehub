using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Looks up and records DLQ error cluster signatures for a namespace, answering
/// "is this a new problem, or one I already know about?" for each observed cluster.
/// </summary>
public interface INamespaceSignatureLookupService
{
    /// <summary>
    /// Given a namespace and the signatures observed in a scan, upserts each one —
    /// incrementing <see cref="SignatureLookupResult.OccurrenceCount"/> and refreshing
    /// last-seen for known hashes, inserting a new record for unseen ones — and returns the
    /// new-vs-recurring result for every distinct hash in <paramref name="observations"/>.
    /// Scoped to <paramref name="ownerId"/>: a signature observed by one owner never affects
    /// another owner's result.
    /// </summary>
    /// <param name="ownerId">The owner whose signatures are being recorded.</param>
    /// <param name="namespaceId">The namespace the observations were made in.</param>
    /// <param name="observations">The signatures observed in this scan.</param>
    /// <param name="hashKind">
    /// Which identity vocabulary every hash in <paramref name="observations"/> belongs to (M1.4,
    /// ADR-0009 §Decision unit 2) — a single call is always all-Fingerprint or all-Cluster, never
    /// mixed, because each caller computes its hashes with exactly one builder
    /// (<c>FailureFingerprintBuilder</c> or <c>ClusterSignatureHasher</c>).
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyDictionary<string, SignatureLookupResult>> LookupAndRecordAsync(
        string ownerId,
        Guid namespaceId,
        IReadOnlyList<ClusterSignatureObservation> observations,
        SignatureHashKind hashKind,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a single persisted signature record without recording an observation — unlike
    /// <see cref="LookupAndRecordAsync"/>, this never increments <c>OccurrenceCount</c> or
    /// moves <c>LastSeenAt</c>. Returns <see langword="null"/> if this hash has never been
    /// observed for the given owner/namespace.
    /// </summary>
    Task<NamespaceSignature?> GetByHashAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds every persisted record of this exact signature hash in namespaces other than
    /// <paramref name="excludeNamespaceId"/>, scoped to <paramref name="ownerId"/> — the
    /// fleet-wide counterpart to <see cref="GetByHashAsync"/>, answering "has this signature
    /// been observed anywhere else in my fleet?"
    /// </summary>
    Task<IReadOnlyList<NamespaceSignature>> FindAcrossNamespacesAsync(
        string ownerId,
        string signatureHash,
        Guid excludeNamespaceId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A single cluster's signature as observed in a scan, ready for lookup/upsert.
/// </summary>
public sealed record ClusterSignatureObservation(
    string SignatureHash,
    string DominantDeadletterReason,
    IReadOnlyList<string> TopTerms);

/// <summary>
/// The new-vs-recurring result for one signature hash.
/// </summary>
public sealed record SignatureLookupResult(
    bool IsNew,
    DateTimeOffset FirstSeenAt,
    int OccurrenceCount);
