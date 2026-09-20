using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Deterministic implementation of <see cref="IBacklogForecastService"/>: fits a linear trend
/// over trailing arrival-rate buckets to project when an entity's active backlog will cross the
/// alert threshold. No ML, no LLM — every number here is arithmetic over
/// <see cref="DlqDbContext.DlqMessages"/> counts, reproducible by anyone re-running the same
/// query (roadmap §5.E, P4).
/// </summary>
public sealed class DeterministicBacklogForecastService : IBacklogForecastService
{
    /// <summary>Number of equal-length trailing buckets used to fit the growth-rate trend.</summary>
    private const int TrendBuckets = 4;

    /// <summary>
    /// Below this backlog depth, a projected breach is treated as noise rather than a signal —
    /// a queue going from 1 to 3 active messages is not a trend worth pushing.
    /// </summary>
    private const int MinimumSignalCount = 5;

    /// <summary>
    /// Floor on the fitted growth rate (messages/hour) before a projection is trusted. Below
    /// this, tiny/rounding-level slopes would otherwise produce absurdly long or unstable
    /// projected breach times.
    /// </summary>
    private const double MinimumGrowthRatePerHour = 0.1;

    /// <summary>How far into the future a projected breach is still considered actionable.</summary>
    private const int MaxHorizonHours = 168;

    /// <summary>Default alert threshold when the caller does not supply one.</summary>
    internal const int DefaultAlertThreshold = 100;

    private readonly DlqDbContext _dbContext;

    public DeterministicBacklogForecastService(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<BacklogForecast>>> ForecastAsync(
        Guid namespaceId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int? alertThreshold = null,
        CancellationToken cancellationToken = default)
    {
        if (endTime <= startTime)
        {
            return Result.Failure<IReadOnlyList<BacklogForecast>>(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "endTime must be after startTime."));
        }

        var threshold = alertThreshold ?? DefaultAlertThreshold;
        if (threshold <= 0)
        {
            return Result.Failure<IReadOnlyList<BacklogForecast>>(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "alertThreshold must be a positive number of messages."));
        }

        var bucketLength = endTime - startTime;

        DateTimeOffset trendStart;
        try
        {
            checked
            {
                trendStart = startTime - TimeSpan.FromTicks(bucketLength.Ticks * (TrendBuckets - 1));
            }
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            return Result.Failure<IReadOnlyList<BacklogForecast>>(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "The requested time window is too large to compute a growth trend for."));
        }

        // Raw timestamps pulled and bucketed in memory, matching DeterministicAnomalyDetectionService:
        // SQLite cannot reliably translate DateTimeOffset bucketing into SQL, so bucketing is done
        // client-side over a bounded, already-indexed row set.
        var arrivalsByEntity = await _dbContext.DlqMessages
            .AsNoTracking()
            .Where(m => m.NamespaceId == namespaceId
                && m.DetectedAtUtc >= trendStart
                && m.DetectedAtUtc < endTime)
            .Select(m => new { m.EntityName, m.DetectedAtUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var activeCountsByEntity = await _dbContext.DlqMessages
            .AsNoTracking()
            .Where(m => m.NamespaceId == namespaceId && m.Status == DlqMessageStatus.Active)
            .GroupBy(m => m.EntityName)
            .Select(g => new { EntityName = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.EntityName, g => g.Count, cancellationToken)
            .ConfigureAwait(false);

        var forecasts = new List<BacklogForecast>();
        var bucketHours = bucketLength.TotalHours;

        foreach (var group in arrivalsByEntity.GroupBy(m => m.EntityName))
        {
            var bucketCounts = new double[TrendBuckets];
            for (var i = 0; i < TrendBuckets; i++)
            {
                var periodStart = trendStart + TimeSpan.FromTicks(bucketLength.Ticks * i);
                var periodEnd = trendStart + TimeSpan.FromTicks(bucketLength.Ticks * (i + 1));
                bucketCounts[i] = group.Count(m => m.DetectedAtUtc >= periodStart && m.DetectedAtUtc < periodEnd);
            }

            if (!activeCountsByEntity.TryGetValue(group.Key, out var currentBacklogCount))
            {
                continue;
            }

            if (currentBacklogCount < MinimumSignalCount || currentBacklogCount >= threshold)
            {
                continue;
            }

            var slopePerBucket = ComputeLinearSlope(bucketCounts);
            var growthRatePerHour = bucketHours > 0 ? slopePerBucket / bucketHours : 0;

            if (growthRatePerHour < MinimumGrowthRatePerHour)
            {
                continue;
            }

            var hoursToBreach = (threshold - currentBacklogCount) / growthRatePerHour;
            if (hoursToBreach <= 0 || hoursToBreach > MaxHorizonHours)
            {
                continue;
            }

            forecasts.Add(BuildForecast(namespaceId, group.Key, currentBacklogCount, growthRatePerHour, threshold, hoursToBreach));
        }

        return Result.Success<IReadOnlyList<BacklogForecast>>(forecasts);
    }

    private static BacklogForecast BuildForecast(
        Guid namespaceId,
        string entityName,
        int currentBacklogCount,
        double growthRatePerHour,
        int threshold,
        double hoursToBreach)
    {
        // Sooner breaches are more severe: at the horizon edge severity floors near 10, an
        // imminent breach approaches 100.
        var urgency = 1.0 - Math.Clamp(hoursToBreach / MaxHorizonHours, 0.0, 1.0);
        var severity = (int)Math.Clamp(Math.Round(10 + urgency * 90), 10, 100);

        var description =
            $"Entity '{entityName}' has {currentBacklogCount} unresolved dead-lettered message(s) and is " +
            $"growing at ~{growthRatePerHour:F1}/hour. At this rate it will cross the alert threshold of " +
            $"{threshold} in approximately {hoursToBreach:F1} hour(s).";

        var metrics = new Dictionary<string, double>
        {
            ["currentBacklogCount"] = currentBacklogCount,
            ["growthRatePerHour"] = growthRatePerHour,
            ["alertThreshold"] = threshold,
            ["projectedHoursToBreach"] = hoursToBreach,
            ["trendBuckets"] = TrendBuckets,
        };

        var recommendedActions = new[]
        {
            "Review recent producer/consumer deployments for this entity.",
            "Consider scheduling a bulk replay or scaling remediation before the threshold is reached.",
            "Check DLQ Intelligence for a newly dominant failure signature driving the growth.",
        };

        return BacklogForecast.Create(
            namespaceId,
            entityName,
            currentBacklogCount,
            growthRatePerHour,
            threshold,
            hoursToBreach,
            severity,
            description,
            metrics,
            recommendedActions);
    }

    /// <summary>
    /// Ordinary least-squares slope of <paramref name="values"/> against their bucket index
    /// (0, 1, 2, ...) — the simplest linear fit for "messages added per bucket" trend.
    /// </summary>
    private static double ComputeLinearSlope(IReadOnlyList<double> values)
    {
        var n = values.Count;
        if (n < 2)
        {
            return 0;
        }

        double sumX = 0, sumY = 0, sumXY = 0, sumXX = 0;
        for (var i = 0; i < n; i++)
        {
            sumX += i;
            sumY += values[i];
            sumXY += i * values[i];
            sumXX += (double)i * i;
        }

        var denominator = (n * sumXX) - (sumX * sumX);
        if (denominator == 0)
        {
            return 0;
        }

        return ((n * sumXY) - (sumX * sumY)) / denominator;
    }
}
