using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// Deterministic, template-based implementation of <see cref="INarrationService"/> (roadmap
/// §5.B, I4 — "Narrate"). Stitches I1–I3's structured output and P1/P2/C1 findings into plain
/// English via fixed sentence templates — no ML, no LLM, no external call.
/// </summary>
public sealed class DeterministicNarrationService : INarrationService
{
    private const int MaxRecommendedActions = 3;

    /// <inheritdoc />
    public IReadOnlyList<Narration> GenerateNarrations(
        IReadOnlyDictionary<Guid, Namespace> namespacesById,
        IReadOnlyList<Anomaly> anomalies,
        IReadOnlyList<DriftFinding> driftFindings,
        IReadOnlyList<CorrelationFinding> correlationFindings)
    {
        ArgumentNullException.ThrowIfNull(namespacesById);
        ArgumentNullException.ThrowIfNull(anomalies);
        ArgumentNullException.ThrowIfNull(driftFindings);
        ArgumentNullException.ThrowIfNull(correlationFindings);

        var narrations = new List<Narration>();

        var namespaceIds = anomalies.Select(a => a.NamespaceId)
            .Concat(driftFindings.Select(d => d.NamespaceId))
            .Distinct();

        foreach (var namespaceId in namespaceIds)
        {
            var nsAnomalies = anomalies.Where(a => a.NamespaceId == namespaceId).ToList();
            var nsDrift = driftFindings.Where(d => d.NamespaceId == namespaceId).ToList();

            narrations.Add(BuildNamespaceActivityNarration(namespaceId, namespacesById, nsAnomalies, nsDrift));
        }

        foreach (var correlation in correlationFindings)
        {
            narrations.Add(BuildCorrelationNarration(correlation, namespacesById));
        }

        return narrations;
    }

    private static Narration BuildNamespaceActivityNarration(
        Guid namespaceId,
        IReadOnlyDictionary<Guid, Namespace> namespacesById,
        IReadOnlyList<Anomaly> anomalies,
        IReadOnlyList<DriftFinding> driftFindings)
    {
        var namespaceName = namespacesById.TryGetValue(namespaceId, out var ns) ? ns.Name : namespaceId.ToString();

        var findings = anomalies
            .Select(a => (EntityName: a.EntityName, Severity: a.Severity, Description: a.Description, Actions: a.RecommendedActions))
            .Concat(driftFindings.Select(d => (EntityName: d.EntityName, Severity: d.Severity, Description: d.Description, Actions: d.RecommendedActions)))
            .ToList();

        var maxSeverity = findings.Max(f => f.Severity);
        var worst = findings.OrderByDescending(f => f.Severity).First();
        var entityCount = findings.Select(f => f.EntityName).Distinct().Count();

        var headline = (anomalies.Count, driftFindings.Count) switch
        {
            ( > 0, > 0) => $"{anomalies.Count} anomaly(ies) and {driftFindings.Count} drift finding(s) in '{namespaceName}'",
            ( > 0, 0) => $"{anomalies.Count} anomaly(ies) detected in '{namespaceName}'",
            _ => $"{driftFindings.Count} drift finding(s) detected in '{namespaceName}'",
        };

        var recommendedActions = findings
            .OrderByDescending(f => f.Severity)
            .SelectMany(f => f.Actions)
            .Distinct(StringComparer.Ordinal)
            .Take(MaxRecommendedActions)
            .ToList();

        var summary =
            $"In the most recent detection cycle, {findings.Count} finding(s) were observed across " +
            $"{entityCount} entit{(entityCount == 1 ? "y" : "ies")} in namespace '{namespaceName}'. " +
            $"The most severe is on '{worst.EntityName}' (severity {worst.Severity}/100): {worst.Description}" +
            (recommendedActions.Count > 0
                ? $" Recommended: {string.Join("; ", recommendedActions)}."
                : ".");

        return Narration.Create(
            NarrationKind.NamespaceActivity,
            namespaceId,
            [namespaceId],
            headline,
            summary,
            maxSeverity,
            contributingAnomalyIds: anomalies.Select(a => a.Id).ToList(),
            contributingDriftFindingIds: driftFindings.Select(d => d.Id).ToList(),
            recommendedActions: recommendedActions);
    }

    private static Narration BuildCorrelationNarration(
        CorrelationFinding correlation,
        IReadOnlyDictionary<Guid, Namespace> namespacesById)
    {
        var memberNamespaceIds = correlation.Members.Select(m => m.NamespaceId).Distinct().ToList();

        var entityDescriptions = correlation.Members
            .Select(m =>
            {
                var name = namespacesById.TryGetValue(m.NamespaceId, out var ns) ? ns.Name : m.NamespaceId.ToString();
                return $"'{m.EntityName}' in '{name}' ({m.AnomalyType}, severity {m.Severity})";
            })
            .ToList();

        var headline = $"{correlation.Members.Count} entities across {memberNamespaceIds.Count} namespace(s) show a correlated {correlation.Provider} pattern";

        var recommendedActions = correlation.RecommendedActions.Take(MaxRecommendedActions).ToList();

        var summary = $"{correlation.Description} Affected: {string.Join("; ", entityDescriptions)}." +
            (recommendedActions.Count > 0 ? $" Recommended: {string.Join("; ", recommendedActions)}." : string.Empty);

        return Narration.Create(
            NarrationKind.CrossNamespaceCorrelation,
            namespaceId: null,
            accessNamespaceIds: memberNamespaceIds,
            headline: headline,
            summary: summary,
            severity: correlation.Severity,
            contributingCorrelationFindingIds: [correlation.Id],
            recommendedActions: recommendedActions);
    }
}
