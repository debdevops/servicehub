namespace ServiceHub.Core.Models;

/// <summary>
/// One failure signature's evidence, shaped for <see cref="Interfaces.IReasoningAgentClient"/> —
/// deliberately a projection of <see cref="DTOs.Responses.IncidentDetailResponse"/>/
/// <see cref="DTOs.Responses.AttentionQueueItem"/>, not a reuse of either: every field here is a
/// count, a status string, or an already-normalised term — never a message body, never raw
/// payload content. See roadmap §7: "message bodies [never] reach this companion — only
/// FeatureRecord-shaped fingerprints". <see cref="Ref"/> is an opaque token the reasoning
/// companion round-trips in its response instead of interpreting; ServiceHub resolves it back to
/// <see cref="OwnerId"/>/<see cref="NamespaceId"/>/<see cref="SignatureHash"/> itself.
/// </summary>
public sealed record ReasoningEvidenceRecord(
    string Ref,
    string OwnerId,
    Guid NamespaceId,
    string SignatureHash,
    string LifecycleStatus,
    string Severity,
    string? Provider,
    string? DominantDeadletterReason,
    IReadOnlyList<string> TopTerms,
    int OccurrenceCount,
    int BlastRadius,
    bool IsRecurring,
    int PendingDecisionCount,
    int RecoveryEntryCount,
    int OpenRecoveryEntryCount,
    int AnomalyFlagCount,
    int DriftFindingCount,
    int CorrelationHypothesisCount,
    int PreventionTriggerCount,
    int ReplayPlanCount);

/// <summary>
/// One advisory observation the reasoning companion produced for one <see cref="ReasoningEvidenceRecord"/>.
/// Deliberately has no confidence/probability field anywhere in its shape — roadmap §7's
/// non-negotiable "no autonomy transition is ever driven by a model's stated confidence" applies
/// even to a field nothing currently reads, since a field that exists gets used eventually.
/// </summary>
public sealed record ReasoningProposal(
    string Ref,
    string Summary,
    IReadOnlyList<string> Considerations);
