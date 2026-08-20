using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure;

/// <summary>
/// Builds the cross-namespace fleet overview by aggregating persisted DLQ intelligence
/// (<see cref="DlqDbContext"/>) against the owner's namespaces (<see cref="INamespaceRepository"/>).
/// <para>
/// The query is deliberately bounded to <i>currently relevant</i> rows — anything still Active,
/// plus anything detected or resolved inside the trend/window horizon — and aggregated in
/// memory. This mirrors the existing <c>DlqHistoryService.GetSummaryAsync</c> approach (SQLite
/// does not translate date-part grouping), keeps the working set small for a self-hosted
/// single-team deployment, and avoids brittle provider-specific GROUP BY translations.
/// </para>
/// </summary>
public sealed class FleetOverviewService : IFleetOverviewService
{
    private const int TrendDays = 7;
    private const int CriticalNewInWindow = 10;
    private const int CriticalActiveBacklog = 50;

    private readonly DlqDbContext _dbContext;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly CloudProviderRouter _router;
    private readonly IConfiguration _configuration;
    private readonly ILogger<FleetOverviewService> _logger;

    /// <summary>Initializes a new instance of the <see cref="FleetOverviewService"/> class.</summary>
    public FleetOverviewService(
        DlqDbContext dbContext,
        INamespaceRepository namespaceRepository,
        CloudProviderRouter router,
        IConfiguration configuration,
        ILogger<FleetOverviewService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<Result<FleetOverview>> GetOverviewAsync(
        string ownerId,
        int windowHours = 24,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            return Result<FleetOverview>.Failure(
                Error.Validation("Fleet.OwnerRequired", "Owner identifier is required."));
        }

        try
        {
            windowHours = Math.Clamp(windowHours, 1, 720);
            var now = DateTimeOffset.UtcNow;
            var windowCutoff = now.AddHours(-windowHours);
            var trendCutoff = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero).AddDays(-(TrendDays - 1));
            var relevantCutoff = windowCutoff < trendCutoff ? windowCutoff : trendCutoff;

            // Known limitation: fleet aggregation is not yet allow-list-aware — a
            // namespace-restricted key still sees cross-namespace category/trend aggregates for
            // its full owner pool here. See docs/KNOWN-LIMITATIONS.md.
            var namespacesResult = await _namespaceRepository.GetByOwnerAsync(ownerId, allowedNamespaceIds: null, cancellationToken);
            var namespaces = namespacesResult.IsSuccess
                ? namespacesResult.Value
                : [];

            // Bounded projection: only rows that can affect the overview.
            var rows = await _dbContext.DlqMessages.AsNoTracking()
                .Where(m => m.OwnerId == ownerId)
                .Where(m => m.Status == DlqMessageStatus.Active
                    || m.DetectedAtUtc >= relevantCutoff
                    || (m.ReplayedAt != null && m.ReplayedAt >= relevantCutoff)
                    || (m.ResolvedAt != null && m.ResolvedAt >= relevantCutoff))
                .Select(m => new RelevantRow(
                    m.NamespaceId,
                    m.Status,
                    m.EntityName,
                    m.FailureCategory,
                    m.DetectedAtUtc,
                    m.ReplayedAt,
                    m.ResolvedAt))
                .ToListAsync(cancellationToken);

            // All-time total per namespace (one small grouped query).
            var totalsByNamespace = await _dbContext.DlqMessages.AsNoTracking()
                .Where(m => m.OwnerId == ownerId)
                .GroupBy(m => m.NamespaceId)
                .Select(g => new { NamespaceId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.NamespaceId, x => x.Count, cancellationToken);

            var rowsByNamespace = rows.GroupBy(r => r.NamespaceId)
                .ToDictionary(g => g.Key, g => g.ToList());

            var namespaceHealth = new List<FleetNamespaceHealth>();

            // Only currently-registered namespaces are surfaced in the live fleet rollup.
            // DLQ rows for a namespace that no longer exists are preserved as-is in SQLite
            // (DlqMonitorWorker archives rather than deletes orphaned rows) so no forensic
            // history is lost, but they must never appear as a namespace row here or inflate
            // NamespaceCount/"at risk of N" — those numbers must always equal the namespaces
            // that actually exist right now.
            var namespaceIds = namespaces.Select(n => n.Id);

            foreach (var namespaceId in namespaceIds)
            {
                var ns = namespaces.FirstOrDefault(n => n.Id == namespaceId);
                rowsByNamespace.TryGetValue(namespaceId, out var nsRows);
                nsRows ??= [];

                var activeRows = nsRows.Where(r => r.Status == DlqMessageStatus.Active).ToList();
                var activeCount = activeRows.Count;
                var newInWindow = nsRows.Count(r => r.DetectedAtUtc >= windowCutoff);
                var resolvedInWindow = nsRows.Count(r =>
                    (r.ReplayedAt.HasValue && r.ReplayedAt.Value >= windowCutoff)
                    || (r.ResolvedAt.HasValue && r.ResolvedAt.Value >= windowCutoff));

                var topEntity = activeRows
                    .GroupBy(r => r.EntityName)
                    .Select(g => new { Entity = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .FirstOrDefault();

                var topCategory = activeRows
                    .GroupBy(r => r.FailureCategory)
                    .Select(g => new { Category = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .FirstOrDefault();

                var oldestActive = activeRows.Count > 0
                    ? activeRows.Min(r => r.DetectedAtUtc)
                    : (DateTimeOffset?)null;

                var (coverage, coverageNote) = DetermineCoverage(ns?.Provider ?? CloudProviderType.Azure);

                namespaceHealth.Add(new FleetNamespaceHealth(
                    NamespaceId: namespaceId,
                    NamespaceName: ns?.DisplayName ?? ns?.Name ?? "(deleted namespace)",
                    Provider: (ns?.Provider ?? CloudProviderType.Azure).ToString(),
                    Environment: (ns?.Environment ?? EnvironmentType.Dev).ToString(),
                    ActiveCount: activeCount,
                    NewInWindow: newInWindow,
                    ResolvedInWindow: resolvedInWindow,
                    TotalCount: totalsByNamespace.GetValueOrDefault(namespaceId, activeCount),
                    TopEntity: topEntity?.Entity,
                    TopEntityCount: topEntity?.Count ?? 0,
                    TopCategory: topCategory?.Category.ToString(),
                    OldestActiveDetectedAt: oldestActive,
                    Severity: DetermineSeverity(activeCount, newInWindow, coverage),
                    Coverage: coverage,
                    CoverageNote: coverageNote));
            }

            var ordered = namespaceHealth
                .OrderByDescending(n => n.Severity)
                .ThenByDescending(n => n.NewInWindow)
                .ThenByDescending(n => n.ActiveCount)
                .ThenBy(n => n.NamespaceName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var topCategories = rows
                .Where(r => r.Status == DlqMessageStatus.Active)
                .GroupBy(r => r.FailureCategory)
                .Select(g => new { Category = g.Key.ToString(), Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .Take(8)
                .ToDictionary(x => x.Category, x => x.Count);

            var overview = new FleetOverview(
                GeneratedAt: now,
                WindowHours: windowHours,
                NamespaceCount: namespaceHealth.Count,
                TotalActive: ordered.Sum(n => n.ActiveCount),
                TotalNewInWindow: ordered.Sum(n => n.NewInWindow),
                TotalResolvedInWindow: ordered.Sum(n => n.ResolvedInWindow),
                Namespaces: ordered,
                TopCategories: topCategories,
                DailyTrend: BuildTrend(rows, now));

            return overview;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to build fleet overview");
            return Result<FleetOverview>.Failure(
                Error.Internal("Fleet.OverviewFailed", $"Failed to build fleet overview: {ex.Message}"));
        }
    }

    private static FleetHealthSeverity DetermineSeverity(int activeCount, int newInWindow, FleetMonitoringCoverage coverage)
    {
        if (newInWindow >= CriticalNewInWindow || activeCount >= CriticalActiveBacklog)
            return FleetHealthSeverity.Critical;
        if (activeCount > 0 || newInWindow > 0)
            return FleetHealthSeverity.Warning;

        // Zero known dead-letters is only "Healthy" if the namespace was actually scanned — a
        // namespace that was never observed must never render the same as a clean one (blueprint
        // Gap 1: this is the one place ServiceHub could make an affirmative false safety claim).
        return coverage == FleetMonitoringCoverage.Scanned
            ? FleetHealthSeverity.Healthy
            : FleetHealthSeverity.Unknown;
    }

    /// <summary>
    /// Mirrors the coverage check <c>DlqMonitorService.ScanNamespaceAsync</c> performs before
    /// scanning a namespace, so Fleet Health never claims visibility it doesn't have.
    /// </summary>
    private (FleetMonitoringCoverage Coverage, string? Note) DetermineCoverage(CloudProviderType provider)
    {
        if (!_router.IsRegistered(provider))
        {
            return (FleetMonitoringCoverage.ProviderNotRegistered, $"No provider is registered for '{provider}'.");
        }

        var capabilities = _router.Resolve(provider).Capabilities;
        if (!capabilities.SupportsRepeatablePeek
            && !_configuration.GetValue($"DlqMonitor:AllowDestructivePeek:{provider}", false))
        {
            return (FleetMonitoringCoverage.NotMonitored, capabilities.Notes);
        }

        return (FleetMonitoringCoverage.Scanned, null);
    }

    private static List<FleetTrendPoint> BuildTrend(List<RelevantRow> rows, DateTimeOffset now)
    {
        var today = now.UtcDateTime.Date;
        var startDate = today.AddDays(-(TrendDays - 1));

        var newByDay = rows
            .Where(r => r.DetectedAtUtc.UtcDateTime.Date >= startDate)
            .GroupBy(r => r.DetectedAtUtc.UtcDateTime.Date)
            .ToDictionary(g => g.Key, g => g.Count());

        var resolvedByDay = rows
            .SelectMany(r => ResolvedInstant(r) is { } d ? new[] { d.UtcDateTime.Date } : [])
            .Where(d => d >= startDate)
            .GroupBy(d => d)
            .ToDictionary(g => g.Key, g => g.Count());

        var trend = new List<FleetTrendPoint>(TrendDays);
        for (var i = 0; i < TrendDays; i++)
        {
            var day = startDate.AddDays(i);
            trend.Add(new FleetTrendPoint(
                Date: new DateTimeOffset(day, TimeSpan.Zero),
                NewMessages: newByDay.GetValueOrDefault(day, 0),
                ResolvedMessages: resolvedByDay.GetValueOrDefault(day, 0)));
        }

        return trend;
    }

    private static DateTimeOffset? ResolvedInstant(RelevantRow r)
        => r.ResolvedAt ?? r.ReplayedAt;

    private sealed record RelevantRow(
        Guid NamespaceId,
        DlqMessageStatus Status,
        string EntityName,
        FailureCategory FailureCategory,
        DateTimeOffset DetectedAtUtc,
        DateTimeOffset? ReplayedAt,
        DateTimeOffset? ResolvedAt);
}
