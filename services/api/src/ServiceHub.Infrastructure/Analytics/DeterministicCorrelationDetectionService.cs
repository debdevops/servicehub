using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Deterministic implementation of <see cref="ICorrelationDetectionService"/>: a pure grouping
/// pass over anomalies an <see cref="IAnomalyDetectionService"/> run already produced. No
/// database access, no new data source — every finding is a reproducible function of the
/// <see cref="AnomalyObservation"/> list passed in (roadmap §5.D, C1).
/// </summary>
public sealed class DeterministicCorrelationDetectionService : ICorrelationDetectionService
{
    /// <summary>Minimum number of distinct entities anomalous in the same cycle before it counts as a correlation.</summary>
    private const int MinimumMembers = 2;

    /// <inheritdoc />
    public IReadOnlyList<CorrelationFinding> DetectCorrelations(IReadOnlyList<AnomalyObservation> observations)
    {
        ArgumentNullException.ThrowIfNull(observations);

        var findings = new List<CorrelationFinding>();

        // Same owner + same provider, per the roadmap's C1 scope ("same-provider proactive
        // correlation" — C2 generalizes this to cross-provider later by dropping the Provider
        // key from this grouping, the same technique applied a second time).
        foreach (var group in observations.GroupBy(o => (o.OwnerId, o.Provider)))
        {
            // One anomaly per entity per cycle (IAnomalyDetectionService's own contract), so
            // distinct entities is just distinct anomalies here — but de-duplicate by
            // (NamespaceId, EntityName) defensively in case a caller merges more than one
            // detection pass into a single DetectCorrelations call.
            var distinctMembers = group
                .GroupBy(o => (o.Anomaly.NamespaceId, o.Anomaly.EntityName))
                .Select(g => g.First())
                .OrderBy(o => o.Anomaly.NamespaceId)
                .ThenBy(o => o.Anomaly.EntityName, StringComparer.Ordinal)
                .ToList();

            if (distinctMembers.Count < MinimumMembers)
            {
                continue;
            }

            findings.Add(BuildCorrelationFinding(group.Key.OwnerId, group.Key.Provider, distinctMembers));
        }

        return findings;
    }

    private static CorrelationFinding BuildCorrelationFinding(
        string ownerId,
        CloudProviderType provider,
        IReadOnlyList<AnomalyObservation> observations)
    {
        var members = observations
            .Select(o => new CorrelationMember(o.Anomaly.NamespaceId, o.Anomaly.EntityName, o.Anomaly.Type, o.Anomaly.Severity))
            .ToList();

        var severity = members.Max(m => m.Severity);
        var entityList = string.Join("', '", members.Select(m => m.EntityName));

        var description =
            $"{members.Count} entities on {provider} were anomalous in the same detection cycle: " +
            $"'{entityList}'. Surfaced as one candidate incident — confirm whether these share a " +
            "downstream cause before treating them as unrelated.";

        var metrics = new Dictionary<string, double>
        {
            ["memberCount"] = members.Count,
            ["maxMemberSeverity"] = severity,
        };

        var recommendedActions = new[]
        {
            "Check whether these entities share a downstream dependency (consumer, database, third-party API).",
            "Review deploys or config changes around the detection window that could affect all of them at once.",
            "If unrelated, no action is needed — this is a temporal hypothesis, not a confirmed shared cause.",
        };

        return CorrelationFinding.Create(ownerId, provider, members, severity, description, metrics, recommendedActions);
    }
}
