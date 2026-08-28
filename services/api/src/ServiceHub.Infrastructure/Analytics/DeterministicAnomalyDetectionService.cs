using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Deterministic implementation of <see cref="IAnomalyDetectionService"/>: flags an entity's
/// message volume as anomalous when the current window's count deviates from the mean of the
/// trailing baseline windows by more than a statistical or relative threshold. No ML, no LLM —
/// every number here is derived from <see cref="DlqDbContext.DlqMessages"/> counts, reproducible
/// by anyone re-running the same query (roadmap §5.B, I3).
/// </summary>
public sealed class DeterministicAnomalyDetectionService : IAnomalyDetectionService
{
    /// <summary>Number of trailing windows (equal in length to the current window) used as the baseline.</summary>
    private const int BaselinePeriods = 4;

    /// <summary>
    /// Below this count in both the current window and the baseline mean, a swing is treated as
    /// noise rather than a signal — a queue going from 1 message to 3 is not an anomaly.
    /// </summary>
    private const int MinimumSignalCount = 5;

    /// <summary>How many standard deviations above/below the baseline mean counts as anomalous.</summary>
    private const double StdDevThreshold = 2.0;

    /// <summary>
    /// Floor on the deviation threshold as a fraction of the baseline mean, so a baseline with
    /// near-zero variance (e.g. a perfectly steady 10/day) still requires a meaningful relative
    /// swing — not just any deviation from a standard deviation of ~0 — before it is flagged.
    /// </summary>
    private const double RelativeThresholdFraction = 0.5;

    private readonly DlqDbContext _dbContext;

    public DeterministicAnomalyDetectionService(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<Anomaly>>> DetectAnomaliesAsync(
        Guid namespaceId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        if (endTime <= startTime)
        {
            return Result.Failure<IReadOnlyList<Anomaly>>(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "endTime must be after startTime."));
        }

        var windowLength = endTime - startTime;
        var baselineStart = startTime - TimeSpan.FromTicks(windowLength.Ticks * BaselinePeriods);

        // Raw timestamps pulled and grouped in memory, matching DlqHistoryService.GetSummaryAsync's
        // approach: SQLite cannot reliably translate DateTimeOffset arithmetic/bucketing into SQL,
        // so bucketing by period is done client-side over a bounded, already-indexed row set
        // (IX_DlqMessages_Owner_Namespace_Status / DetectedAtUtc).
        var timestampsByEntity = await _dbContext.DlqMessages
            .AsNoTracking()
            .Where(m => m.NamespaceId == namespaceId
                && m.DetectedAtUtc >= baselineStart
                && m.DetectedAtUtc < endTime)
            .Select(m => new { m.EntityName, m.DetectedAtUtc })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var anomalies = new List<Anomaly>();

        foreach (var group in timestampsByEntity.GroupBy(m => m.EntityName))
        {
            var baselineCounts = new List<int>(BaselinePeriods);
            for (var i = BaselinePeriods; i >= 1; i--)
            {
                var periodStart = startTime - TimeSpan.FromTicks(windowLength.Ticks * i);
                var periodEnd = startTime - TimeSpan.FromTicks(windowLength.Ticks * (i - 1));
                baselineCounts.Add(group.Count(m => m.DetectedAtUtc >= periodStart && m.DetectedAtUtc < periodEnd));
            }

            var currentCount = group.Count(m => m.DetectedAtUtc >= startTime && m.DetectedAtUtc < endTime);

            var mean = baselineCounts.Average();
            if (mean < MinimumSignalCount && currentCount < MinimumSignalCount)
            {
                continue;
            }

            var stdDev = ComputeStandardDeviation(baselineCounts, mean);
            var threshold = Math.Max(stdDev * StdDevThreshold, Math.Max(mean * RelativeThresholdFraction, MinimumSignalCount));
            var deviation = currentCount - mean;

            AnomalyType? type = deviation switch
            {
                > 0 when deviation > threshold => AnomalyType.HighMessageVolume,
                < 0 when -deviation > threshold && mean >= MinimumSignalCount => AnomalyType.LowMessageVolume,
                _ => null,
            };

            if (type is null)
            {
                continue;
            }

            anomalies.Add(BuildAnomaly(namespaceId, group.Key, type.Value, currentCount, mean, stdDev, threshold));
        }

        return Result.Success<IReadOnlyList<Anomaly>>(anomalies);
    }

    private static Anomaly BuildAnomaly(
        Guid namespaceId,
        string entityName,
        AnomalyType type,
        int currentCount,
        double mean,
        double stdDev,
        double threshold)
    {
        var deviation = currentCount - mean;
        var magnitude = threshold > 0 ? Math.Abs(deviation) / threshold : 1.0;
        var severity = (int)Math.Clamp(Math.Round(magnitude * 40), 10, 100);

        var direction = type == AnomalyType.HighMessageVolume ? "spike" : "drop";
        var description =
            $"Entity '{entityName}' had {currentCount} dead-lettered message(s) in the current period, " +
            $"a {direction} versus a baseline mean of {mean:F1} (±{stdDev:F1}) over the preceding " +
            $"{BaselinePeriods} period(s) of equal length.";

        var metrics = new Dictionary<string, double>
        {
            ["currentCount"] = currentCount,
            ["baselineMean"] = mean,
            ["baselineStdDev"] = stdDev,
            ["baselinePeriods"] = BaselinePeriods,
            ["deviationThreshold"] = threshold,
        };

        var recommendedActions = type == AnomalyType.HighMessageVolume
            ? new[]
            {
                "Review recent producer/consumer deployments for this entity.",
                "Check DLQ Intelligence for a newly dominant failure signature.",
                "Consider scoping an AutoReplayRule if the cause looks transient.",
            }
            : new[]
            {
                "Verify the entity's upstream producer is still emitting traffic.",
                "Confirm no monitoring or ingestion gap is masking real failures.",
            };

        return Anomaly.Create(namespaceId, entityName, type, severity, description, metrics, recommendedActions);
    }

    private static double ComputeStandardDeviation(IReadOnlyList<int> values, double mean)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        var sumOfSquares = values.Sum(v => Math.Pow(v - mean, 2));
        return Math.Sqrt(sumOfSquares / values.Count);
    }
}
