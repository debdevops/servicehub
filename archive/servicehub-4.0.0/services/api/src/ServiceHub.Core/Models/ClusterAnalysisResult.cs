namespace ServiceHub.Core.Models;

/// <summary>
/// Raw result of an AI service <c>/analyze</c> call: the clusters/singletons the service
/// grouped the batch into, plus the request-scoped ref → <see cref="Entities.DlqMessage.Id"/>
/// map needed to translate refs back into real messages. Carries no history or explanation —
/// those are composed by the caller.
/// </summary>
public sealed record ClusterAnalysisResult(
    IReadOnlyList<ClusterSummary> Clusters,
    IReadOnlyList<ClusterSingleton> Singletons,
    string Method,
    IReadOnlyDictionary<string, long> RefToMessageId);

/// <summary>One error-signature cluster from the AI service's response.</summary>
public sealed record ClusterSummary(
    int Size,
    string RepresentativeRef,
    IReadOnlyList<string> TopTerms,
    string FirstOccurrenceRef,
    string LastOccurrenceRef,
    string DominantEntity,
    string DominantDeadletterReason,
    IReadOnlyList<string> MemberRefs,
    int DominantDeadletterReasonCount);

/// <summary>A message the AI service could not group into any cluster.</summary>
public sealed record ClusterSingleton(
    string Ref,
    string DominantEntity,
    string DominantDeadletterReason);
