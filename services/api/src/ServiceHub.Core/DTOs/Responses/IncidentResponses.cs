namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Cheap counts over <see cref="IncidentDetailResponse.RecoveryEntries"/>/<see cref="IncidentDetailResponse.PlaybookEntries"/>
/// — no separate query, purely a fold over what the detail response already carries. Saves the
/// caller from re-deriving "is there anything here that needs a human" from the raw lists.
/// </summary>
/// <param name="RecoveryEntryCount">Total recovery entries recorded against this signature.</param>
/// <param name="OpenRecoveryEntryCount">Recovery entries still <c>Executing</c>/<c>Observing</c> — not yet terminal.</param>
/// <param name="PendingDecisionCount">Recovery entries the Eligibility Gate declined (awaiting manual replay/purge)
/// plus Playbook entries still <c>Proposed</c>/<c>UnderReview</c>/<c>Edited</c> — everything this incident is
/// currently blocked on a human for.</param>
/// <param name="AnomalyFlagCount">Playbook entries proposing <c>AnomalyFlag</c>.</param>
/// <param name="DriftFindingCount">Playbook entries proposing <c>DriftFinding</c>.</param>
/// <param name="CorrelationHypothesisCount">Playbook entries proposing <c>CorrelationHypothesis</c>.</param>
/// <param name="PreventionTriggerCount">Playbook entries proposing <c>PreventionTrigger</c>.</param>
/// <param name="ReplayPlanCount">Playbook entries proposing <c>ReplayPlan</c>.</param>
public sealed record IncidentSummary(
    int RecoveryEntryCount,
    int OpenRecoveryEntryCount,
    int PendingDecisionCount,
    int AnomalyFlagCount,
    int DriftFindingCount,
    int CorrelationHypothesisCount,
    int PreventionTriggerCount,
    int ReplayPlanCount);

/// <summary>
/// Response DTO for <c>GET /api/v1/namespaces/{namespaceId}/incidents/{signatureHash}</c> (W2.1) — one
/// durable, addressable view of everything ServiceHub knows about a failure signature: its identity and
/// lifecycle status (<see cref="Entities.NamespaceSignature"/>/<see cref="Entities.SignatureLifecycleState"/>),
/// what it did about it (<see cref="Entities.RecoveryLedgerEntry"/>), and what it proposed or found about it
/// (<see cref="Entities.PlaybookEntry"/> — anomalies, drift, correlation hypotheses, prevention triggers,
/// replay plans). A projection over existing data: no new store, no migration, no new write path.
/// </summary>
public sealed record IncidentDetailResponse(
    string SignatureHash,
    Guid NamespaceId,
    string? NamespaceName,
    string LifecycleStatus,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastSeenAt,
    int OccurrenceCount,
    string? DominantDeadletterReason,
    IReadOnlyList<string> TopTerms,
    IncidentSummary Summary,
    IReadOnlyList<RecoveryLedgerEntryResponse> RecoveryEntries,
    IReadOnlyList<PlaybookEntryResponse> PlaybookEntries);
