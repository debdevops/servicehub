using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Response for the Investigation Center — aggregated signatures, replay failures, knowledge gaps,
/// and recent activity, ordered by priority and actionability.
/// </summary>
public sealed record InvestigationCenterResponse(
    CompactMetricsSummary Metrics,
    IReadOnlyList<InvestigationQueueItem> InvestigationQueue,
    IReadOnlyList<FailedReplayItem> FailedReplays,
    IReadOnlyList<KnowledgeReviewItem> KnowledgeReview,
    IReadOnlyList<NewSignatureItem> NewSignatures,
    IReadOnlyList<RecentChangeItem> RecentlyChanged,
    FleetHealthSummary? FleetHealth);

/// <summary>
/// Fleet-wide health signal for the Investigation Center's Fleet Health section, composed from
/// <see cref="IFleetOverviewService"/> — the same <see cref="FleetNamespaceHealth"/>/severity data
/// <c>FleetPage</c> already renders, capped to the worst-N namespaces. Null if the fleet overview
/// query fails; the rest of the Investigation Center response is unaffected.
/// </summary>
public sealed record FleetHealthSummary(
    int NamespaceCount,
    int TotalActive,
    int TotalNewInWindow,
    int TotalResolvedInWindow,
    IReadOnlyList<FleetNamespaceHealth> TopUnhealthyNamespaces);

/// <summary>Compact aggregated metrics shown in the header.</summary>
public sealed record CompactMetricsSummary(
    int TotalSignatures,
    int ActiveSignatures,
    int ResolvedSignatures,
    int SuppressedSignatures,
    int ArchivedSignatures,
    int RequiresAction);

/// <summary>
/// An active signature requiring investigation. Ordered by computed priority score.
/// Priority factors: escalation, no knowledge, high occurrence count, age.
/// </summary>
public sealed record InvestigationQueueItem(
    string SignatureHash,
    Guid NamespaceId,
    string DisplayName,
    string DominantDeadletterReason,
    int MessageCount,
    string Status,
    string Trend,
    double PriorityScore,
    bool HasKnowledge,
    bool IsEscalating,
    string? Owner,
    string? RecommendedNextAction,
    string? Explanation);

/// <summary>A replay job that failed in the monitoring window (last 7 days).</summary>
public sealed record FailedReplayItem(
    Guid JobId,
    Guid NamespaceId,
    string SignatureHash,
    string SignatureName,
    string JobStatus,
    string? FailureReason,
    DateTimeOffset CreatedAt,
    DateTimeOffset? CompletedAt,
    int AttemptedCount,
    int FailedCount,
    string? RecommendedNextAction);

/// <summary>A signature with overdue or missing knowledge, ordered by urgency.</summary>
public sealed record KnowledgeReviewItem(
    string SignatureHash,
    Guid NamespaceId,
    string DisplayName,
    int MessageCount,
    string Status,
    string? Owner,
    bool HasKnowledge,
    bool IsReviewOverdue,
    DateTimeOffset? ReviewDueAt,
    DateTimeOffset? LastUpdatedAt,
    string? RecommendedNextAction);

/// <summary>A signature with no knowledge attached (new or previously unreviewed).</summary>
public sealed record NewSignatureItem(
    string SignatureHash,
    Guid NamespaceId,
    string DisplayName,
    string DominantDeadletterReason,
    int MessageCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    string? Explanation,
    string? RecommendedNextAction);

/// <summary>A recent change: lifecycle transition, knowledge update, or replay completion.</summary>
public sealed record RecentChangeItem(
    string ChangeType,
    string SignatureHash,
    Guid NamespaceId,
    string SignatureName,
    DateTimeOffset OccurredAt,
    string? Details,
    string? Actor);
