using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Deterministic implementation of <see cref="IDriftDetectionService"/>: establishes each
/// entity's "accepted" message shape as the dominant <c>SchemaFingerprint</c>/<c>PayloadShape</c>
/// over the trailing baseline periods, then flags the current window when a meaningful share of
/// its messages diverge from that baseline. No ML, no LLM, no new data source — every number
/// here is derived from <see cref="MessageFeatureRecord"/> rows already persisted by
/// <c>DlqMonitorService</c> at detection time (roadmap §5.C, P1/P2).
/// </summary>
/// <remarks>
/// "Accepted" here means the shape an entity's dead-lettered messages have consistently had
/// until now — not the shape of messages that were successfully processed. Baselining the shape
/// of live, non-dead-lettered traffic would require a new continuously-sampled data source (a
/// peek-and-fingerprint pipeline over every active queue), which the roadmap explicitly rules
/// out for this item ("no new data source — derived from message bodies ServiceHub already
/// reads"). A shape that was normal for an entity's failures until now and then changes is still
/// exactly the leading indicator P2 is after: a producer shipped a breaking change, and the DLQ
/// population is the first place it shows up.
/// </remarks>
public sealed class DeterministicDriftDetectionService : IDriftDetectionService
{
    /// <summary>Number of trailing windows (equal in length to the current window) used as the baseline.</summary>
    private const int BaselinePeriods = 4;

    /// <summary>
    /// Minimum number of feature records required on both the baseline and current side before a
    /// comparison is attempted — below this, any shape swing is noise rather than a signal.
    /// </summary>
    private const int MinimumSignalCount = 5;

    /// <summary>
    /// A baseline (or current) window's dominant shape must cover at least this share of the
    /// window's records before it is treated as "the" shape for that window. Below this, the
    /// entity's traffic is inherently heterogeneous and a drift comparison would be noise.
    /// </summary>
    private const double DominantShareThreshold = 0.5;

    /// <summary>
    /// Share of current-window records whose schema fingerprint falls outside the baseline's
    /// dominant fingerprint before a schema-shape drift is flagged.
    /// </summary>
    private const double SchemaDriftShareThreshold = 0.4;

    private readonly DlqDbContext _dbContext;

    public DeterministicDriftDetectionService(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<DriftFinding>>> DetectDriftAsync(
        Guid namespaceId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        if (endTime <= startTime)
        {
            return Result.Failure<IReadOnlyList<DriftFinding>>(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "endTime must be after startTime."));
        }

        var windowLength = endTime - startTime;

        DateTimeOffset baselineStart;
        try
        {
            checked
            {
                baselineStart = startTime - TimeSpan.FromTicks(windowLength.Ticks * BaselinePeriods);
            }
        }
        catch (Exception ex) when (ex is OverflowException or ArgumentOutOfRangeException)
        {
            return Result.Failure<IReadOnlyList<DriftFinding>>(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "The requested time window is too large to compute a baseline for."));
        }

        // Raw rows pulled and grouped in memory, matching DeterministicAnomalyDetectionService's
        // approach: SQLite cannot reliably translate DateTimeOffset arithmetic into SQL, so
        // bucketing by period is done client-side over a bounded, already-indexed row set
        // (IX_MessageFeatureRecords_Namespace_CapturedAt / Namespace_EntityName).
        var records = await _dbContext.MessageFeatureRecords
            .AsNoTracking()
            .Where(f => f.NamespaceId == namespaceId
                && f.CapturedAt >= baselineStart
                && f.CapturedAt < endTime)
            .Select(f => new
            {
                f.EntityName,
                f.CapturedAt,
                f.SchemaFingerprint,
                f.PayloadShape,
                f.BodySizeBytes,
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var findings = new List<DriftFinding>();

        foreach (var group in records.GroupBy(r => r.EntityName))
        {
            var baseline = group.Where(r => r.CapturedAt < startTime).ToList();
            var current = group.Where(r => r.CapturedAt >= startTime && r.CapturedAt < endTime).ToList();

            if (baseline.Count < MinimumSignalCount || current.Count < MinimumSignalCount)
            {
                continue;
            }

            var (baselineFingerprint, baselineFingerprintShare) = DominantValue(baseline.Select(r => r.SchemaFingerprint));
            var (baselinePayloadShape, baselinePayloadShare) = DominantValue(baseline.Select(r => r.PayloadShape));

            if (baselineFingerprintShare >= DominantShareThreshold)
            {
                var driftCount = current.Count(r => r.SchemaFingerprint != baselineFingerprint);
                var driftShare = (double)driftCount / current.Count;

                if (driftShare > SchemaDriftShareThreshold)
                {
                    var baselineSizeMean = baseline.Average(r => (double)r.BodySizeBytes);
                    var currentSizeMean = current.Average(r => (double)r.BodySizeBytes);

                    findings.Add(BuildSchemaShapeDriftFinding(
                        namespaceId, group.Key, driftCount, current.Count, driftShare,
                        baselineFingerprint, baselineFingerprintShare, baselineSizeMean, currentSizeMean));
                }
            }

            var (currentPayloadShape, currentPayloadShare) = DominantValue(current.Select(r => r.PayloadShape));

            if (baselinePayloadShare >= DominantShareThreshold
                && currentPayloadShare >= DominantShareThreshold
                && !string.Equals(baselinePayloadShape, currentPayloadShape, StringComparison.Ordinal))
            {
                findings.Add(BuildPayloadFormatDriftFinding(
                    namespaceId, group.Key, baselinePayloadShape, baselinePayloadShare,
                    currentPayloadShape, currentPayloadShare, current.Count));
            }
        }

        return Result.Success<IReadOnlyList<DriftFinding>>(findings);
    }

    private static DriftFinding BuildSchemaShapeDriftFinding(
        Guid namespaceId,
        string entityName,
        int driftCount,
        int currentTotal,
        double driftShare,
        string baselineFingerprint,
        double baselineFingerprintShare,
        double baselineSizeMean,
        double currentSizeMean)
    {
        // driftShare is already > SchemaDriftShareThreshold (40) by the caller's firing
        // condition, so the clamp's floor of 0 is a genuine safety net, not an implied reachable
        // low end — deliberately not set to e.g. 20/40, which would misstate the actual range.
        var severity = (int)Math.Clamp(Math.Round(driftShare * 100), 0, 100);

        var description =
            $"Entity '{entityName}' has {driftCount} of {currentTotal} recent dead-lettered " +
            $"message(s) ({driftShare:P0}) with a schema shape not matching the baseline's " +
            $"dominant fingerprint '{baselineFingerprint}', which covered {baselineFingerprintShare:P0} " +
            $"of the preceding {BaselinePeriods} period(s) of equal length.";

        var metrics = new Dictionary<string, double>
        {
            ["driftCount"] = driftCount,
            ["currentTotal"] = currentTotal,
            ["driftShare"] = driftShare,
            ["baselineFingerprintShare"] = baselineFingerprintShare,
            ["baselinePeriods"] = BaselinePeriods,
            ["baselineMeanBodySizeBytes"] = baselineSizeMean,
            ["currentMeanBodySizeBytes"] = currentSizeMean,
        };

        var recommendedActions = new[]
        {
            "Review recent producer deployments for this entity — a field may have been added, renamed, or removed.",
            "Compare a recent message body against the DLQ Intelligence Failure Signature baseline for this entity.",
            "If intentional, no action is needed — the baseline rolls forward automatically on the next detection cycle.",
        };

        return DriftFinding.Create(
            namespaceId, entityName, DriftFindingType.SchemaShapeDrift, severity, description, metrics, recommendedActions);
    }

    private static DriftFinding BuildPayloadFormatDriftFinding(
        Guid namespaceId,
        string entityName,
        string baselinePayloadShape,
        double baselinePayloadShare,
        string currentPayloadShape,
        double currentPayloadShare,
        int currentTotal)
    {
        // currentPayloadShare is already >= DominantShareThreshold (50) by the caller's firing
        // condition, so the clamp's floor of 0 is a genuine safety net, not an implied reachable
        // low end — deliberately not set to e.g. 40, which would misstate the actual range.
        var severity = (int)Math.Clamp(Math.Round(currentPayloadShare * 100), 0, 100);

        var description =
            $"Entity '{entityName}' shifted from a baseline dominant payload format of " +
            $"'{baselinePayloadShape}' ({baselinePayloadShare:P0} of the preceding {BaselinePeriods} " +
            $"period(s)) to '{currentPayloadShape}' ({currentPayloadShare:P0} of the current period) " +
            "— a likely producer contract change.";

        var metrics = new Dictionary<string, double>
        {
            ["baselinePayloadShare"] = baselinePayloadShare,
            ["currentPayloadShare"] = currentPayloadShare,
            ["currentTotal"] = currentTotal,
            ["baselinePeriods"] = BaselinePeriods,
        };

        var recommendedActions = new[]
        {
            "Confirm the producer's content-type/serialization change was intentional — this is a stronger breaking-change signal than a field-level shape drift.",
            "Verify consumer deserialization logic can handle the new format before replaying any affected messages.",
        };

        return DriftFinding.Create(
            namespaceId, entityName, DriftFindingType.PayloadFormatDrift, severity, description, metrics, recommendedActions);
    }

    /// <summary>
    /// Returns the most frequent value in <paramref name="values"/> and its share of the total
    /// count. Returns (empty string, 0) for an empty sequence. Ties are broken by ordinal value
    /// (not encounter order) so the result is reproducible regardless of the row order the
    /// underlying query happens to return — required for this class's "deterministic, no ML"
    /// guarantee to hold even at an exact share tie (e.g. two shapes each at 50%).
    /// </summary>
    private static (string Value, double Share) DominantValue(IEnumerable<string> values)
    {
        var counts = values
            .GroupBy(v => v, StringComparer.Ordinal)
            .Select(g => new { Value = g.Key, Count = g.Count() })
            .ToList();

        if (counts.Count == 0)
        {
            return (string.Empty, 0);
        }

        var total = counts.Sum(c => c.Count);
        var dominant = counts
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Value, StringComparer.Ordinal)
            .First();
        return (dominant.Value, (double)dominant.Count / total);
    }
}
