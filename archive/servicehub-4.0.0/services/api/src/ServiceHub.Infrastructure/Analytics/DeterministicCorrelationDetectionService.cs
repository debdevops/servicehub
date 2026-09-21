using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Deterministic implementation of <see cref="ICorrelationDetectionService"/>: a pure grouping
/// pass over anomalies an <see cref="IAnomalyDetectionService"/> run already produced. No
/// database access, no new data source — every finding is a reproducible function of the
/// <see cref="AnomalyObservation"/> list passed in (roadmap §5.D, C1 same-provider correlation,
/// generalized by C2 cross-cloud correlation).
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

        // Same owner only (roadmap C2 — "Cross-cloud correlation"): C1 grouped by (OwnerId,
        // Provider) as well, which meant two entities anomalous in the same cycle under the same
        // owner but on different clouds were never linked. Dropping Provider from the grouping
        // key is the whole generalization — each observation still carries its own Provider
        // (used per-member below), so a same-provider finding still reports one provider, it's
        // just no longer a precondition for grouping at all.
        foreach (var group in observations.GroupBy(o => o.OwnerId))
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

            findings.Add(BuildCorrelationFinding(group.Key, distinctMembers));
        }

        return findings;
    }

    private static CorrelationFinding BuildCorrelationFinding(
        string ownerId,
        IReadOnlyList<AnomalyObservation> observations)
    {
        var members = observations
            .Select(o => new CorrelationMember(o.Anomaly.NamespaceId, o.Anomaly.EntityName, o.Anomaly.Type, o.Anomaly.Severity, o.Provider))
            .ToList();

        var severity = members.Max(m => m.Severity);
        var entityList = string.Join("', '", members.Select(m => m.EntityName));
        var providers = members.Select(m => m.Provider).Distinct().OrderBy(p => (int)p).ToList();

        var description = providers.Count == 1
            ? $"{members.Count} entities on {providers[0]} were anomalous in the same detection cycle: " +
              $"'{entityList}'. Surfaced as one candidate incident — confirm whether these share a " +
              "downstream cause before treating them as unrelated."
            : $"{members.Count} entities across {providers.Count} cloud providers ({string.Join("/", providers)}) " +
              $"were anomalous in the same detection cycle: '{entityList}'. Surfaced as one candidate incident " +
              "spanning multiple clouds — confirm whether these share a downstream cause (e.g. a shared " +
              "upstream producer or consumer) before treating them as unrelated.";

        var metrics = new Dictionary<string, double>
        {
            ["memberCount"] = members.Count,
            ["maxMemberSeverity"] = severity,
            ["providerCount"] = providers.Count,
        };

        var recommendedActions = new[]
        {
            "Check whether these entities share a downstream dependency (consumer, database, third-party API).",
            "Review deploys or config changes around the detection window that could affect all of them at once.",
            "If unrelated, no action is needed — this is a temporal hypothesis, not a confirmed shared cause.",
        };

        return CorrelationFinding.Create(ownerId, members, severity, description, metrics, recommendedActions);
    }
}
