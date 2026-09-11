using ServiceHub.Core.Enums;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Cross-cloud dead-letter overview — groups the fleet's DLQ backlog by provider so an operator
/// can see, at a glance, which cloud is generating the most dead-letters, why, and where.
/// Complements <see cref="IFleetOverviewService"/> (flat, per-namespace, "what changed overnight")
/// by answering the triage-first question: "which cloud, which reasons, which namespaces, and is
/// it getting better or worse?"
/// </summary>
public interface IDlqOverviewService
{
    /// <summary>Builds a provider-grouped DLQ overview for the given owner.</summary>
    /// <param name="ownerId">Tenant/owner identifier for isolation.</param>
    /// <param name="filter">Scope and time-window filters. See <see cref="DlqOverviewFilter"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<DlqOverview>> GetOverviewAsync(
        string ownerId,
        DlqOverviewFilter filter,
        CancellationToken cancellationToken = default);
}

/// <summary>Filters narrowing a <see cref="DlqOverview"/> query. All optional/additive.</summary>
public sealed record DlqOverviewFilter(
    int Days = 7,
    CloudProviderType? Provider = null,
    EnvironmentType? Environment = null,
    Guid? NamespaceId = null,
    FailureCategory? Reason = null);

/// <summary>Cross-cloud dead-letter snapshot, grouped by provider.</summary>
public sealed record DlqOverview(
    DateTimeOffset GeneratedAt,
    int WindowDays,
    DlqOverviewTotals Totals,
    IReadOnlyList<DlqProviderOverview> Providers);

/// <summary>Fleet-wide totals rolled up across every provider included in the response.</summary>
public sealed record DlqOverviewTotals(
    int TotalDeadLettered,
    double? ChangePercent,
    int NamespacesWithDlq,
    int NamespacesTotal,
    int AffectedQueues,
    int AffectedTopics,
    DateTimeOffset? OldestMessageDetectedAt);

/// <summary>
/// One cloud provider's active dead-letter backlog: current size, trend, top failure reasons, and
/// the namespaces contributing to it. Only providers with at least one registered namespace (after
/// filtering) are included — a provider nobody has connected never renders an empty section.
/// </summary>
public sealed record DlqProviderOverview(
    CloudProviderType Provider,
    int TotalDeadLettered,
    double? ChangePercent,
    int NamespacesWithDlq,
    int NamespacesTotal,
    int AffectedQueues,
    int AffectedTopics,
    IReadOnlyList<DlqOverviewTrendPoint> DailyTrend,
    IReadOnlyList<DlqReasonBreakdown> TopReasons,
    IReadOnlyList<DlqOverviewNamespace> Namespaces);

/// <summary>
/// A single day's reconstructed active-backlog size (messages detected on/before this day that
/// were still unresolved at day's end) — a running-backlog line, not a daily-arrivals bar.
/// </summary>
public sealed record DlqOverviewTrendPoint(DateTimeOffset Date, int Count);

/// <summary>One failure-category slice of a provider's active backlog.</summary>
public sealed record DlqReasonBreakdown(FailureCategory Category, int Count, double Percent);

/// <summary>One namespace's dead-letter footprint within a provider section. Namespaces with zero
/// active dead-letters (after filtering) are omitted — this lists namespaces *with* DLQ.</summary>
public sealed record DlqOverviewNamespace(
    Guid NamespaceId,
    string NamespaceName,
    EnvironmentType Environment,
    int QueuesWithDlq,
    int TopicsWithDlq,
    int DlqCount,
    DateTimeOffset? OldestDetectedAt);
