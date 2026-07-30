namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Looks up and records DLQ error cluster signatures for a namespace, answering
/// "is this a new problem, or one I already know about?" for each observed cluster.
/// </summary>
public interface INamespaceSignatureLookupService
{
    /// <summary>
    /// Given a namespace and the cluster signatures observed in a scan, upserts each one —
    /// incrementing <see cref="SignatureLookupResult.OccurrenceCount"/> and refreshing
    /// last-seen for known hashes, inserting a new record for unseen ones — and returns the
    /// new-vs-recurring result for every distinct hash in <paramref name="observations"/>.
    /// Scoped to <paramref name="ownerId"/>: a signature observed by one owner never affects
    /// another owner's result.
    /// </summary>
    Task<IReadOnlyDictionary<string, SignatureLookupResult>> LookupAndRecordAsync(
        string ownerId,
        Guid namespaceId,
        IReadOnlyList<ClusterSignatureObservation> observations,
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
