namespace ServiceHub.Core.Models;

/// <summary>
/// Fully composed DLQ error-signature analysis for a namespace: every cluster resolved to
/// real message IDs, its new-vs-recurring history, and a human-readable explanation; plus
/// unclustered singletons kept as a distinct group. <see cref="Available"/> is
/// <see langword="false"/> when the AI service could not be reached — the caller renders its
/// unavailable state from this instead of treating it as an error.
/// </summary>
public sealed record DlqSignatureAnalysisResult(
    bool Available,
    string? Method,
    int BatchSize,
    IReadOnlyList<DlqClusterSignature> Clusters,
    IReadOnlyList<DlqSingletonSignature> Singletons);

/// <summary>One error-signature cluster, enriched with message identity, history, and explanation.</summary>
public sealed record DlqClusterSignature(
    int Size,
    IReadOnlyList<long> MessageIds,
    string DominantEntity,
    string DominantDeadletterReason,
    int DominantDeadletterReasonCount,
    IReadOnlyList<string> TopTerms,
    bool IsNew,
    DateTimeOffset FirstSeenAt,
    int OccurrenceCount,
    DateTimeOffset WindowStart,
    DateTimeOffset WindowEnd,
    string Explanation);

/// <summary>A message the AI service could not group into any cluster, resolved to its message ID.</summary>
public sealed record DlqSingletonSignature(
    long MessageId,
    string DominantEntity,
    string DominantDeadletterReason);
