using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Deterministic, template-based narration over I1–I3's structured output plus P1/P2 drift and
/// C1 correlation findings (roadmap §5.B, I4 — "Narrate"). No ML, no LLM.
/// </summary>
public interface INarrationService
{
    /// <summary>
    /// Stitches anomalies, drift findings, and correlation findings into one narration artifact
    /// per emergent pattern: one per namespace that has any anomaly or drift finding, plus one
    /// per correlation finding.
    /// </summary>
    /// <param name="namespacesById">Lookup used to resolve human-readable namespace names.</param>
    /// <param name="anomalies">Anomalies detected in the current cycle, across namespaces.</param>
    /// <param name="driftFindings">Drift findings detected in the current cycle, across namespaces.</param>
    /// <param name="correlationFindings">Correlation findings detected in the current cycle.</param>
    /// <returns>The generated narrations. Empty if nothing was found.</returns>
    IReadOnlyList<Narration> GenerateNarrations(
        IReadOnlyDictionary<Guid, Namespace> namespacesById,
        IReadOnlyList<Anomaly> anomalies,
        IReadOnlyList<DriftFinding> driftFindings,
        IReadOnlyList<CorrelationFinding> correlationFindings);
}
