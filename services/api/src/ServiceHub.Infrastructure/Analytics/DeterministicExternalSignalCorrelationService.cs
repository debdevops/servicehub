using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Deterministic implementation of <see cref="IExternalSignalCorrelationService"/> — a pure
/// grouping pass over anomalies and signals already resolved by the caller. No database access,
/// no new data source of its own (roadmap §5.D, C3).
/// </summary>
public sealed class DeterministicExternalSignalCorrelationService : IExternalSignalCorrelationService
{
    /// <inheritdoc />
    public IReadOnlyList<ExternalSignalCorrelation> DetectCorrelations(
        IReadOnlyList<AnomalyObservation> observations,
        IReadOnlyList<ExternalSignalEvent> signals,
        TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(signals);

        if (window <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(window), "Correlation window must be positive.");
        }

        var correlations = new List<ExternalSignalCorrelation>();

        var signalsByOwner = signals.GroupBy(s => s.OwnerId).ToDictionary(g => g.Key, g => g.ToList());

        foreach (var observation in observations)
        {
            if (!signalsByOwner.TryGetValue(observation.OwnerId, out var ownerSignals))
            {
                continue;
            }

            var anomaly = observation.Anomaly;

            var nearest = ownerSignals
                .Where(s => s.NamespaceId is null || s.NamespaceId == anomaly.NamespaceId)
                .Where(s => s.OccurredAt <= anomaly.DetectedAt)
                .Where(s => anomaly.DetectedAt - s.OccurredAt <= window)
                .OrderByDescending(s => s.OccurredAt)
                .FirstOrDefault();

            if (nearest is null)
            {
                continue;
            }

            var gap = anomaly.DetectedAt - nearest.OccurredAt;

            var description =
                $"Entity '{anomaly.EntityName}' became anomalous {FormatGap(gap)} after a " +
                $"{nearest.SignalType} signal from '{nearest.Source}'. Surfaced as a candidate cause — " +
                "confirm whether this deploy/config change is actually responsible before treating it as one.";

            var recommendedActions = new[]
            {
                $"Review the {nearest.SignalType.ToString().ToLowerInvariant()} recorded at {nearest.OccurredAt:O} for changes affecting '{anomaly.EntityName}'.",
                "If unrelated, no action is needed — this is a temporal hypothesis, not a confirmed shared cause.",
                "If confirmed, consider a rollback or targeted fix rather than treating this as a recurring anomaly signature.",
            };

            correlations.Add(ExternalSignalCorrelation.Create(
                observation.OwnerId,
                anomaly.NamespaceId,
                anomaly.EntityName,
                anomaly.Type,
                anomaly.Severity,
                observation.Provider,
                nearest,
                gap,
                description,
                recommendedActions));
        }

        return correlations;
    }

    private static string FormatGap(TimeSpan gap) => gap switch
    {
        { TotalMinutes: < 1 } => "less than a minute",
        { TotalHours: < 1 } => $"{(int)gap.TotalMinutes}m",
        _ => $"{(int)gap.TotalHours}h{gap.Minutes}m",
    };
}
