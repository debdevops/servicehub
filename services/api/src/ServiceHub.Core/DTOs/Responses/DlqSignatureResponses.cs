namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// A namespace's DLQ error-signature analysis. <see cref="Available"/> is <see langword="false"/>
/// when the AI service could not be reached — the frontend renders its unavailable state from
/// this, it is never a non-200 response.
/// </summary>
public sealed record DlqSignaturesResponse(
    bool Available,
    string? Method,
    int BatchSize,
    IReadOnlyList<DlqClusterSignatureResponse> Clusters,
    IReadOnlyList<DlqSingletonSignatureResponse> Singletons);

/// <summary>One error-signature cluster, with message identity, history, and explanation.</summary>
public sealed record DlqClusterSignatureResponse(
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

/// <summary>A message the AI service could not group into any cluster.</summary>
public sealed record DlqSingletonSignatureResponse(
    long MessageId,
    string DominantEntity,
    string DominantDeadletterReason);
