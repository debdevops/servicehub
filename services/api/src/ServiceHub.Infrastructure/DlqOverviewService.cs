using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Builds the cross-cloud DLQ overview by aggregating persisted DLQ intelligence
/// (<see cref="DlqDbContext"/>) against the owner's namespaces (<see cref="INamespaceRepository"/>),
/// grouped by <see cref="CloudProviderType"/>.
/// <para>
/// Like <see cref="FleetOverviewService"/>, the query is bounded to <i>currently relevant</i> rows
/// — anything still Active, plus anything detected/replayed/resolved inside the trend window — and
/// aggregated in memory (SQLite does not translate date-part grouping). The per-day trend is a
/// reconstructed backlog size (messages detected on/before that day, not yet resolved by day's
/// end), not a daily-arrivals count, so the line always lands on the current total on its last
/// point.
/// </para>
/// </summary>
public sealed class DlqOverviewService : IDlqOverviewService
{
    private const int MinWindowDays = 1;
    private const int MaxWindowDays = 90;
    private const int MaxTopReasons = 5;
    private const int MaxRecurringPatterns = 25;
    private const string UnclassifiedRootCause = "Unclassified";

    /// <summary>Severity order for picking a pattern's worst-case replay safety — a mixed group
    /// is reported at its least-safe member's level, never averaged away.</summary>
    private static readonly Dictionary<string, int> ReplaySafetySeverity = new()
    {
        [ReplaySafetyLevels.Unsafe] = 2,
        [ReplaySafetyLevels.RequiresReview] = 1,
        [ReplaySafetyLevels.Safe] = 0,
    };

    private readonly DlqDbContext _dbContext;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<DlqOverviewService> _logger;

    /// <summary>Initializes a new instance of the <see cref="DlqOverviewService"/> class.</summary>
    public DlqOverviewService(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        ILogger<DlqOverviewService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<DlqOverview>> GetOverviewAsync(
        string ownerId,
        DlqOverviewFilter filter,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Result<DlqOverview>.Failure(
                Error.Validation("DlqOverview.OwnerRequired", "Owner identifier is required."));
        }

        try
        {
            var days = Math.Clamp(filter.Days, MinWindowDays, MaxWindowDays);
            var now = DateTimeOffset.UtcNow;
            var today = now.UtcDateTime.Date;
            var startDate = today.AddDays(-(days - 1));
            var startCutoff = new DateTimeOffset(startDate, TimeSpan.Zero);

            var namespacesResult = await _namespaceRepository.GetByOwnerAsync(ownerId, allowedNamespaceIds: null, cancellationToken);
            var namespaces = (namespacesResult.IsSuccess ? namespacesResult.Value : [])
                .Where(n => filter.NamespaceId is null || n.Id == filter.NamespaceId)
                .Where(n => filter.Environment is null || n.Environment == filter.Environment)
                .Where(n => filter.Provider is null || n.Provider == filter.Provider)
                .ToList();

            var namespaceIds = namespaces.Select(n => n.Id).ToHashSet();

            // Bounded projection: only rows that can affect the overview, already restricted to
            // the resolved namespace scope and the reason filter (both server-side translatable).
            List<RelevantRow> rows;
            int totalObserved;
            if (namespaceIds.Count == 0)
            {
                rows = [];
                totalObserved = 0;
            }
            else
            {
                var scopedMessages = _dbContext.DlqMessages.AsNoTracking()
                    .Where(m => m.OwnerId == ownerId)
                    .Where(m => namespaceIds.Contains(m.NamespaceId))
                    .Where(m => filter.Reason == null || m.FailureCategory == filter.Reason)
                    .Where(m => filter.Status == null || m.Status == filter.Status)
                    .Where(m => filter.ReplaySafety == null || m.ReplaySafety == filter.ReplaySafety)
                    .Where(m => filter.EntityName == null || EF.Functions.Like(m.EntityName, $"%{filter.EntityName}%"));

                // Lifetime count across the resolved scope — deliberately unbounded by the trend
                // window (unlike every other metric below), so "Total Observed" answers "how much
                // has this fleet ever dead-lettered", not "...in the selected window".
                totalObserved = await scopedMessages.CountAsync(cancellationToken);

                rows = await scopedMessages
                    .Where(m => m.Status == DlqMessageStatus.Active
                        || m.DetectedAtUtc >= startCutoff
                        || (m.ReplayedAt != null && m.ReplayedAt >= startCutoff)
                        || (m.ResolvedAt != null && m.ResolvedAt >= startCutoff)
                        || (m.ArchivedAt != null && m.ArchivedAt >= startCutoff))
                    .Select(m => new RelevantRow(
                        m.NamespaceId,
                        m.CloudProvider,
                        m.EntityName,
                        m.EntityType,
                        m.TopicName,
                        m.FailureCategory,
                        m.Status,
                        m.DetectedAtUtc,
                        m.ReplayedAt,
                        m.ResolvedAt,
                        m.ArchivedAt,
                        m.ReplaySafety,
                        m.ForensicRootCause,
                        m.DeadLetterReason,
                        m.CategoryConfidence))
                    .ToListAsync(cancellationToken);
            }

            var providers = namespaces
                .Select(n => n.Provider)
                .Distinct()
                .Select(p => BuildProviderOverview(p, namespaces, rows, startDate, today, days))
                .OrderByDescending(p => p.TotalDeadLettered)
                .ThenBy(p => p.Provider)
                .ToList();

            var totalTrend = BuildTrend(rows, startDate, today);
            var totalDeadLettered = providers.Sum(p => p.TotalDeadLettered);

            var replayedRows = rows.Where(r => r.Status == DlqMessageStatus.Replayed && r.ReplayedAt >= startCutoff).ToList();
            var replayedTrend = BuildCumulativeTrend(rows, startDate, today, r => r.Status == DlqMessageStatus.Replayed && r.ReplayedAt is not null, r => r.ReplayedAt!.Value);
            var archivedCount = rows.Count(r => r.Status == DlqMessageStatus.Archived && r.ArchivedAt >= startCutoff);

            var recurringPatterns = BuildRecurringPatterns(rows, totalDeadLettered);

            var totals = new DlqOverviewTotals(
                TotalDeadLettered: totalDeadLettered,
                ChangePercent: totalTrend.Count > 0 ? PercentChange(totalTrend[0].Count, totalDeadLettered) : null,
                NamespacesWithDlq: providers.Sum(p => p.NamespacesWithDlq),
                NamespacesTotal: providers.Sum(p => p.NamespacesTotal),
                AffectedQueues: providers.Sum(p => p.AffectedQueues),
                AffectedTopics: providers.Sum(p => p.AffectedTopics),
                OldestMessageDetectedAt: rows
                    .Where(r => r.Status == DlqMessageStatus.Active)
                    .Select(r => (DateTimeOffset?)r.DetectedAtUtc)
                    .Min(),
                ReplayedCount: replayedRows.Count,
                ReplayedChangePercent: replayedTrend.Count > 0 ? PercentChange(replayedTrend[0].Count, replayedTrend[^1].Count) : null,
                ArchivedCount: archivedCount,
                TotalObserved: totalObserved,
                RecurringPatternCount: recurringPatterns.Count(p => p.Occurrences >= 2),
                NeedsInvestigationCount: recurringPatterns.Count(p => p.ReplaySafety != ReplaySafetyLevels.Safe));

            return new DlqOverview(
                GeneratedAt: now,
                WindowDays: days,
                Totals: totals,
                Providers: providers,
                RecurringPatterns: recurringPatterns);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build DLQ overview");
            return Result<DlqOverview>.Failure(
                Error.Internal("DlqOverview.Failed", $"Failed to build DLQ overview: {ex.Message}"));
        }
    }

    private static DlqProviderOverview BuildProviderOverview(
        CloudProviderType provider,
        List<Namespace> allNamespaces,
        List<RelevantRow> allRows,
        DateTime startDate,
        DateTime today,
        int days)
    {
        var providerNamespaces = allNamespaces.Where(n => n.Provider == provider).ToList();
        var providerRows = allRows.Where(r => r.CloudProvider == provider).ToList();
        var activeRows = providerRows.Where(r => r.Status == DlqMessageStatus.Active).ToList();

        var totalDeadLettered = activeRows.Count;

        var topReasons = activeRows
            .GroupBy(r => r.FailureCategory)
            .Select(g => new { Category = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(MaxTopReasons)
            .Select(x => new DlqReasonBreakdown(
                x.Category,
                x.Count,
                totalDeadLettered > 0 ? Math.Round(100.0 * x.Count / totalDeadLettered, 1) : 0))
            .ToList();

        var namespaceRows = providerNamespaces
            .Select(ns =>
            {
                var nsActiveRows = activeRows.Where(r => r.NamespaceId == ns.Id).ToList();
                return new
                {
                    Namespace = ns,
                    ActiveRows = nsActiveRows
                };
            })
            .Where(x => x.ActiveRows.Count > 0)
            .Select(x => new DlqOverviewNamespace(
                NamespaceId: x.Namespace.Id,
                NamespaceName: x.Namespace.DisplayName ?? x.Namespace.Name,
                Environment: x.Namespace.Environment,
                QueuesWithDlq: x.ActiveRows.Where(r => r.EntityType == ServiceBusEntityType.Queue)
                    .Select(r => r.EntityName).Distinct().Count(),
                TopicsWithDlq: x.ActiveRows.Where(r => r.EntityType == ServiceBusEntityType.Subscription)
                    .Select(r => r.TopicName ?? r.EntityName).Distinct().Count(),
                DlqCount: x.ActiveRows.Count,
                OldestDetectedAt: x.ActiveRows.Min(r => (DateTimeOffset?)r.DetectedAtUtc)))
            .OrderByDescending(n => n.DlqCount)
            .ThenBy(n => n.NamespaceName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var dailyTrend = BuildTrend(providerRows, startDate, today);

        return new DlqProviderOverview(
            Provider: provider,
            TotalDeadLettered: totalDeadLettered,
            ChangePercent: dailyTrend.Count > 0 ? PercentChange(dailyTrend[0].Count, totalDeadLettered) : null,
            NamespacesWithDlq: namespaceRows.Count,
            NamespacesTotal: providerNamespaces.Count,
            AffectedQueues: activeRows.Where(r => r.EntityType == ServiceBusEntityType.Queue)
                .Select(r => r.EntityName).Distinct().Count(),
            AffectedTopics: activeRows.Where(r => r.EntityType == ServiceBusEntityType.Subscription)
                .Select(r => r.TopicName ?? r.EntityName).Distinct().Count(),
            DailyTrend: dailyTrend,
            TopReasons: topReasons,
            Namespaces: namespaceRows);
    }

    /// <summary>
    /// Reconstructs the active-backlog size at the end of each day in <c>[startDate, today]</c>:
    /// rows detected on/before that day which were not yet resolved by that day's end. Rows
    /// outside the bounded fetch (detected and resolved entirely before the window, never Active)
    /// correctly contribute zero to every day in range.
    /// </summary>
    private static List<DlqOverviewTrendPoint> BuildTrend(List<RelevantRow> rows, DateTime startDate, DateTime today)
    {
        var days = (int)(today - startDate).TotalDays + 1;
        var trend = new List<DlqOverviewTrendPoint>(days);

        for (var i = 0; i < days; i++)
        {
            var day = startDate.AddDays(i);
            var count = rows.Count(r =>
            {
                if (r.DetectedAtUtc.UtcDateTime.Date > day)
                    return false;

                var resolved = r.ResolvedAt ?? r.ReplayedAt;
                return resolved is null || resolved.Value.UtcDateTime.Date > day;
            });

            trend.Add(new DlqOverviewTrendPoint(new DateTimeOffset(day, TimeSpan.Zero), count));
        }

        return trend;
    }

    /// <summary>
    /// Cumulative count, per day in <c>[startDate, today]</c>, of rows matching
    /// <paramref name="predicate"/> whose <paramref name="dateSelector"/> falls on or before that
    /// day — a monotonically increasing line, unlike <see cref="BuildTrend"/>'s reconstructed
    /// backlog. Used for activity counters (e.g. replays) where "how many so far" is the
    /// meaningful comparison, not "how many are currently outstanding".
    /// </summary>
    private static List<DlqOverviewTrendPoint> BuildCumulativeTrend(
        List<RelevantRow> rows,
        DateTime startDate,
        DateTime today,
        Func<RelevantRow, bool> predicate,
        Func<RelevantRow, DateTimeOffset> dateSelector)
    {
        var days = (int)(today - startDate).TotalDays + 1;
        var trend = new List<DlqOverviewTrendPoint>(days);
        var matching = rows.Where(predicate).ToList();

        for (var i = 0; i < days; i++)
        {
            var day = startDate.AddDays(i);
            var count = matching.Count(r => dateSelector(r).UtcDateTime.Date <= day);
            trend.Add(new DlqOverviewTrendPoint(new DateTimeOffset(day, TimeSpan.Zero), count));
        }

        return trend;
    }

    private static double? PercentChange(int from, int to)
    {
        if (from == 0)
            return to == 0 ? 0 : null;

        return Math.Round((to - from) / (double)from * 100.0, 1);
    }

    /// <summary>
    /// Groups the fleet's currently-active dead letters by (category, root-cause text) — see the
    /// <see cref="DlqRecurringPattern"/> doc comment for why this, and not the AI clustering
    /// pipeline, is the right basis for a fleet-wide rollup.
    /// </summary>
    private static List<DlqRecurringPattern> BuildRecurringPatterns(
        List<RelevantRow> rows,
        int totalDeadLettered)
    {
        var activeRows = rows.Where(r => r.Status == DlqMessageStatus.Active).ToList();

        return activeRows
            .GroupBy(r => (r.FailureCategory, RootCause: r.ForensicRootCause ?? r.DeadLetterReason ?? UnclassifiedRootCause))
            .Select(g =>
            {
                var occurrences = g.Count();
                var worstSafety = g
                    .Select(r => r.ReplaySafety ?? ReplaySafetyLevels.RequiresReview)
                    .OrderByDescending(s => ReplaySafetySeverity.GetValueOrDefault(s, 1))
                    .First();
                var averageConfidence = g.Average(r => r.CategoryConfidence);
                var representative = g
                    .GroupBy(r => (r.NamespaceId, r.EntityName))
                    .OrderByDescending(grp => grp.Count())
                    .First().Key;

                return new DlqRecurringPattern(
                    PatternKey: $"{g.Key.FailureCategory}:{g.Key.RootCause}",
                    RootCauseSummary: g.Key.RootCause,
                    Category: g.Key.FailureCategory,
                    Occurrences: occurrences,
                    PercentOfTotal: totalDeadLettered > 0 ? Math.Round(100.0 * occurrences / totalDeadLettered, 1) : 0,
                    AffectedProviders: g.Select(r => r.CloudProvider).Distinct().OrderBy(p => p).ToList(),
                    NamespaceCount: g.Select(r => r.NamespaceId).Distinct().Count(),
                    FirstSeenAt: g.Min(r => r.DetectedAtUtc),
                    LastSeenAt: g.Max(r => r.DetectedAtUtc),
                    Confidence: ConfidenceBucket(averageConfidence),
                    AverageConfidence: Math.Round(averageConfidence, 2),
                    ReplaySafety: worstSafety,
                    SuggestedAction: BuildSuggestedAction(worstSafety),
                    RepresentativeNamespaceId: representative.NamespaceId,
                    RepresentativeEntityName: representative.EntityName);
            })
            .OrderByDescending(p => p.Occurrences)
            .ThenBy(p => p.RootCauseSummary, StringComparer.OrdinalIgnoreCase)
            .Take(MaxRecurringPatterns)
            .ToList();
    }

    /// <summary>
    /// Buckets a signature's average per-message classification confidence (0.0–1.0, as recorded
    /// by the forensic engine — see <c>DlqMessage.CategoryConfidence</c>) into a coarse label. A
    /// qualitative bucket, not a fabricated precision score: the underlying pipeline itself only
    /// ever produces a small number of distinct confidence values per rule
    /// (<see cref="ServiceHub.Infrastructure.AI.DeterministicClassifier"/>,
    /// <see cref="ServiceHub.Infrastructure.AI.HeuristicAnalyser"/>), so presenting it as a single
    /// precise percentage across a mixed group would overstate what is actually known.
    /// </summary>
    private static string ConfidenceBucket(double averageConfidence) => averageConfidence switch
    {
        >= 0.85 => "High",
        >= 0.6 => "Medium",
        _ => "Low",
    };

    private static string BuildSuggestedAction(string replaySafety) => replaySafety switch
    {
        ReplaySafetyLevels.Safe => "Safe to auto-replay",
        ReplaySafetyLevels.Unsafe => "Fix the root cause before replaying",
        _ => "Review a sample before replaying",
    };

    private sealed record RelevantRow(
        Guid NamespaceId,
        CloudProviderType CloudProvider,
        string EntityName,
        ServiceBusEntityType EntityType,
        string? TopicName,
        FailureCategory FailureCategory,
        DlqMessageStatus Status,
        DateTimeOffset DetectedAtUtc,
        DateTimeOffset? ReplayedAt,
        DateTimeOffset? ResolvedAt,
        DateTimeOffset? ArchivedAt,
        string? ReplaySafety,
        string? ForensicRootCause,
        string? DeadLetterReason,
        double CategoryConfidence);
}
