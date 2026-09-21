using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// An <see cref="Entities.Anomaly"/> tagged with the owner and cloud provider of the namespace it
/// was detected in — the input <see cref="Interfaces.ICorrelationDetectionService"/> groups over.
/// Kept separate from <see cref="Entities.Anomaly"/> itself because ownership/provider are
/// namespace-level facts the anomaly detector has no reason to know about; the caller (which
/// already resolved the namespace to run detection against it) attaches them.
/// </summary>
/// <param name="Anomaly">The detected anomaly.</param>
/// <param name="OwnerId">The owner of the namespace the anomaly's entity belongs to.</param>
/// <param name="Provider">The cloud provider of the namespace the anomaly's entity belongs to.</param>
public sealed record AnomalyObservation(Anomaly Anomaly, string OwnerId, CloudProviderType Provider);
