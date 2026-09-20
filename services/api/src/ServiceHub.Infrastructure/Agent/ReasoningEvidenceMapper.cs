using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Agent;

/// <summary>
/// Pure projection from <see cref="IncidentDetailResponse"/> (roadmap W2.1's read-model — already
/// an aggregation over signature, recovery, and playbook data with no message-body field anywhere
/// in its shape) to <see cref="ReasoningEvidenceRecord"/>. No I/O, no ledger access — this type
/// exists so the shaping logic is unit-testable independent of <c>ReasoningCompanionWorker</c>'s
/// scope-per-cycle orchestration.
/// </summary>
public static class ReasoningEvidenceMapper
{
    /// <summary>
    /// Builds the opaque <see cref="ReasoningEvidenceRecord.Ref"/> the reasoning companion
    /// round-trips — deliberately encodes nothing sensitive, only the identity a caller already
    /// has to have to have requested this incident in the first place.
    /// </summary>
    public static string BuildRef(Guid namespaceId, string signatureHash) => $"{namespaceId:D}:{signatureHash}";

    public static ReasoningEvidenceRecord ToEvidenceRecord(
        string ownerId, string severity, bool isRecurring, IncidentDetailResponse incident)
    {
        ArgumentNullException.ThrowIfNull(incident);

        return new ReasoningEvidenceRecord(
            Ref: BuildRef(incident.NamespaceId, incident.SignatureHash),
            OwnerId: ownerId,
            NamespaceId: incident.NamespaceId,
            SignatureHash: incident.SignatureHash,
            LifecycleStatus: incident.LifecycleStatus,
            Severity: severity,
            Provider: incident.RecoveryEntries.Count > 0 ? incident.RecoveryEntries[0].ProviderSnapshot : null,
            DominantDeadletterReason: incident.DominantDeadletterReason,
            TopTerms: incident.TopTerms,
            OccurrenceCount: incident.OccurrenceCount,
            BlastRadius: incident.OccurrenceCount,
            IsRecurring: isRecurring,
            PendingDecisionCount: incident.Summary.PendingDecisionCount,
            RecoveryEntryCount: incident.Summary.RecoveryEntryCount,
            OpenRecoveryEntryCount: incident.Summary.OpenRecoveryEntryCount,
            AnomalyFlagCount: incident.Summary.AnomalyFlagCount,
            DriftFindingCount: incident.Summary.DriftFindingCount,
            CorrelationHypothesisCount: incident.Summary.CorrelationHypothesisCount,
            PreventionTriggerCount: incident.Summary.PreventionTriggerCount,
            ReplayPlanCount: incident.Summary.ReplayPlanCount);
    }
}
