using ServiceHub.Core.Enums;

namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Response for the Incident Center's list view (fleet-wide, paginated client-side like
/// <c>DlqOverviewPage</c>/<c>SignatureListPage</c> already do at this scale): every failure
/// signature the owner has, across every registered namespace, plus the trend and category
/// rollups the header charts need. A superset of <see cref="InvestigationCenterResponse"/>'s
/// investigation queue — this includes Resolved/Suppressed/Archived signatures too, since the
/// Incident Center's status tabs need to count and list them.
/// </summary>
public sealed record IncidentListResponse(
    CompactMetricsSummary Metrics,
    IReadOnlyList<IncidentTrendPoint> Trend,
    IReadOnlyList<IncidentCategoryBreakdown> TopCategories,
    IReadOnlyList<IncidentListItem> Items,
    DateTimeOffset GeneratedAt);

/// <summary>
/// One bucket of the incident trend chart. <c>New</c> counts signatures first observed in the
/// bucket (exact, from <c>NamespaceSignature.FirstSeenAt</c>). <c>Resolved</c> counts lifecycle
/// transitions into <c>Resolved</c> recorded in the bucket (exact, from
/// <c>SignatureLifecycleHistory</c>). <c>Active</c> counts signatures that were seen in the
/// bucket (<c>LastSeenAt</c> falls inside it) and whose *current* lifecycle status is Active or
/// Reopened — an "active-and-recently-seen" activity signal, not a reconstructed point-in-time
/// snapshot (this service keeps no historical status-as-of-day index).
/// </summary>
public sealed record IncidentTrendPoint(
    DateTimeOffset BucketStart,
    int Active,
    int Resolved,
    int New);

/// <summary>
/// One entry of the "Top Incident Categories" rollup — total message occurrences (not signature
/// count) attributed to a dominant deadletter reason, mirroring <c>DlqOverviewPage</c>'s
/// top-reasons rollup. The lowest-volume categories beyond the top 5 are folded into "Others".
/// </summary>
public sealed record IncidentCategoryBreakdown(
    string Category,
    int Count,
    double Percent);

/// <summary>
/// One row of the Incident Center's list/table — everything the table and its detail panel's
/// header need for one failure signature, without a second round trip.
/// </summary>
public sealed record IncidentListItem(
    string SignatureHash,
    Guid NamespaceId,
    string? NamespaceName,
    CloudProviderType? CloudProvider,
    EnvironmentType? Environment,
    string DisplayName,
    string Category,
    string Severity,
    string Status,
    bool IsEscalating,
    int MessageCount,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    bool HasKnowledge,
    string? Owner,
    string? RecommendedNextAction);
