using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Deterministic, cross-cloud proactive correlation over already-detected anomalies. No ML, no
/// LLM, no new data source and no new database query: every finding is a reproducible grouping
/// of <see cref="Anomaly"/> instances an <see cref="IAnomalyDetectionService"/> pass already
/// produced for the same detection cycle (roadmap §5.D, C1 — "Same-provider proactive
/// correlation" — generalized by C2, "Cross-cloud correlation", which groups by owner alone so a
/// finding can span providers).
/// </summary>
/// <remarks>
/// Correlation here means temporal co-occurrence, not a proven shared cause: two or more distinct
/// entities under the same owner that were anomalous in the same window are surfaced as one
/// candidate incident instead of N disconnected anomalies an operator has to notice are related —
/// regardless of which cloud provider each entity's namespace lives on. This is a leading
/// hypothesis for a human to confirm or dismiss, never an assertion of causation — consistent
/// with the roadmap's evidence-over-confidence discipline.
/// </remarks>
public interface ICorrelationDetectionService
{
    /// <summary>
    /// Groups <paramref name="observations"/> into same-owner correlation findings: within each
    /// owner's group, two or more distinct entities anomalous in the same cycle become one
    /// <see cref="CorrelationFinding"/>, whether or not they share a cloud provider. A group with
    /// only one anomalous entity produces no finding — a lone anomaly is not a correlation.
    /// </summary>
    /// <param name="observations">
    /// Anomalies detected across one or more namespaces in the same detection cycle, each tagged
    /// with the owner and cloud provider of its namespace.
    /// </param>
    /// <returns>Every correlation found, same-provider or cross-cloud. An empty list is a valid result.</returns>
    IReadOnlyList<CorrelationFinding> DetectCorrelations(IReadOnlyList<AnomalyObservation> observations);
}
