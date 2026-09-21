using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Deterministic external-signal correlation (roadmap §5.D, C3): the same technique C1/C2 use to
/// group anomalies against each other, applied instead to grouping an anomaly against the nearest
/// preceding <see cref="ExternalSignalEvent"/> (a deploy or config change) within a bounded
/// window. No ML, no LLM — a reproducible function of the anomalies and signals passed in. Still
/// 100% observational: a leading hypothesis for a human to confirm or dismiss, never an assertion
/// of causation.
/// </summary>
public interface IExternalSignalCorrelationService
{
    /// <summary>
    /// For each anomaly, finds the nearest external signal that occurred at or before the
    /// anomaly's onset (<see cref="Entities.Anomaly.DetectedAt"/>), within <paramref name="window"/>,
    /// scoped to the same owner and to a signal that is either fleet-wide
    /// (<see cref="ExternalSignalEvent.NamespaceId"/> is null) or matches the anomaly's own
    /// namespace. An anomaly with no matching signal produces no correlation. One anomaly can
    /// match at most one signal (the nearest); one signal can match more than one anomaly.
    /// </summary>
    /// <param name="observations">Anomalies detected across one or more namespaces, each tagged
    /// with the owner and cloud provider of its namespace.</param>
    /// <param name="signals">Candidate external signals to correlate against — typically every
    /// signal recorded for the same owner(s) within the lookback window.</param>
    /// <param name="window">Maximum gap between a signal's <see cref="ExternalSignalEvent.OccurredAt"/>
    /// and an anomaly's onset for the two to be considered correlated.</param>
    /// <returns>Every correlation found. An empty list is a valid result.</returns>
    IReadOnlyList<ExternalSignalCorrelation> DetectCorrelations(
        IReadOnlyList<AnomalyObservation> observations,
        IReadOnlyList<ExternalSignalEvent> signals,
        TimeSpan window);
}
