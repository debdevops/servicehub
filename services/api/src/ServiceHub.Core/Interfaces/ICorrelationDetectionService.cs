using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Deterministic, same-provider proactive correlation over already-detected anomalies. No ML, no
/// LLM, no new data source and no new database query: every finding is a reproducible grouping
/// of <see cref="Anomaly"/> instances an <see cref="IAnomalyDetectionService"/> pass already
/// produced for the same detection cycle (roadmap §5.D, C1 — "Same-provider proactive
/// correlation").
/// </summary>
/// <remarks>
/// Correlation here means temporal co-occurrence, not a proven shared cause: two or more distinct
/// entities under the same owner and cloud provider that were anomalous in the same window are
/// surfaced as one candidate incident instead of N disconnected anomalies an operator has to
/// notice are related. This is a leading hypothesis for a human to confirm or dismiss, never an
/// assertion of causation — consistent with the roadmap's evidence-over-confidence discipline.
/// </remarks>
public interface ICorrelationDetectionService
{
    /// <summary>
    /// Groups <paramref name="observations"/> into same-owner, same-provider correlation
    /// findings: within each (owner, provider) group, two or more distinct entities anomalous in
    /// the same cycle become one <see cref="CorrelationFinding"/>. A group with only one
    /// anomalous entity produces no finding — a lone anomaly is not a correlation.
    /// </summary>
    /// <param name="observations">
    /// Anomalies detected across one or more namespaces in the same detection cycle, each tagged
    /// with the owner and cloud provider of its namespace.
    /// </param>
    /// <returns>Every same-provider correlation found. An empty list is a valid result.</returns>
    IReadOnlyList<CorrelationFinding> DetectCorrelations(IReadOnlyList<AnomalyObservation> observations);
}
