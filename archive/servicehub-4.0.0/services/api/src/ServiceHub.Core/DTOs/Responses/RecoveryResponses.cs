namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Response DTO for a <see cref="Entities.RecoveryOperation"/> — the immutable header for one
/// operator/automation decision to replay or purge.
/// </summary>
public sealed record RecoveryOperationResponse(
    Guid Id,
    string Kind,
    string Trigger,
    string ActorIdentity,
    string ActorKind,
    string? Reason,
    Guid? NamespaceId,
    string? NamespaceNameSnapshot,
    string? ProviderSnapshot,
    string? EnvironmentSnapshot,
    string ScopeDescription,
    long? SourceRuleId,
    long? SourceJobId,
    string ServiceVersion,
    DateTimeOffset OpenedAt,
    int TargetCount,
    int EntryCount);

/// <summary>
/// Response DTO for a <see cref="Entities.RecoveryLedgerEntry"/> — one per (operation, message).
/// No message body, body preview, or connection string ever appears here — see the ledger's
/// payload-exclusion conformance test.
/// </summary>
public sealed record RecoveryLedgerEntryResponse(
    Guid Id,
    Guid OperationId,
    long? DlqMessageId,
    Guid? NamespaceId,
    string? NamespaceNameSnapshot,
    string? ProviderSnapshot,
    string? EnvironmentSnapshot,
    string? EntityNameSnapshot,
    string? EntityTypeSnapshot,
    string? TopicNameSnapshot,
    string BodyHash,
    string? FailureCategorySnapshot,
    string? DeadLetterReasonSnapshot,
    string? SignatureHashSnapshot,
    string TargetEntity,
    DateTimeOffset BegunAt,
    bool MarkerApplied,
    string State,
    string? Disposition,
    string? VerificationResult,
    string? VerificationConfidence,
    DateTimeOffset? ObservationWindowEndsAt,
    DateTimeOffset? ClosedAt);

/// <summary>
/// Response DTO for a <see cref="Entities.RecoveryEvent"/> — one append-only, hash-chained fact.
/// Exposes <c>Seq</c>/<c>PrevHash</c>/<c>EntryHash</c> so the operation detail page can render the
/// chain an auditor would recompute by hand.
/// </summary>
public sealed record RecoveryEventResponse(
    Guid Id,
    string OwnerId,
    long Seq,
    Guid? EntryId,
    Guid OperationId,
    string EventType,
    DateTimeOffset OccurredAt,
    string ActorIdentity,
    string ActorKind,
    string? DetailJson,
    string PrevHash,
    string EntryHash,
    int SchemaVersion);

/// <summary>
/// Response DTO for <c>GET /api/v1/recovery/operations/{id}</c> — the operation header plus its
/// full evidence: every entry begun under it and every event in its chain, Seq-ordered. The list
/// endpoint (<c>GET /api/v1/recovery/operations</c>) still returns the lighter
/// <see cref="RecoveryOperationResponse"/> alone; this composite is for the one-operation detail
/// view, which needs the per-entry table and the event chain in a single round trip.
/// </summary>
public sealed record RecoveryOperationDetailResponse(
    RecoveryOperationResponse Operation,
    IReadOnlyList<RecoveryLedgerEntryResponse> Entries,
    IReadOnlyList<RecoveryEventResponse> Events);

/// <summary>
/// Response DTO for the emergency-stop endpoints (roadmap §9.4.2, §15.2) — the owner-scoped kill
/// switch's live state, derived from the Recovery Evidence Ledger, never a stored flag.
/// </summary>
public sealed record EmergencyStopStatusResponse(bool Active);

/// <summary>
/// Response DTO for <c>GET /api/v1/recovery/trust/{signatureHash}</c> — a
/// <see cref="Models.SignatureTrustEvidence"/> report. <c>UnsafeOutcomePresent</c> is fleet-level
/// (any signature under the owner); <c>DuplicateAssociationPresent</c> is scoped to this
/// signature only (see the model's doc comment).
/// </summary>
public sealed record SignatureTrustEvidenceResponse(
    string SignatureHash,
    string ActionKind,
    int RecoveredCount,
    int ReturnedCount,
    int FailedCount,
    int UnverifiedCount,
    int DeclinedCount,
    int SampleSize,
    double? VerifiedSuccessRate,
    bool MeetsL4SampleAndRate,
    bool MeetsL5SampleAndRate,
    bool UnsafeOutcomePresent,
    bool DuplicateAssociationPresent,
    IReadOnlyList<string> Reasons);

/// <summary>
/// Response DTO for <c>GET /api/v1/recovery/autonomy/{signatureHash}</c> — the actual, currently
/// granted <see cref="Enums.AutonomyLevel"/> for one signature (the Eligibility Gate predicate 5's
/// own read), plus a real, non-fabricated reason when unattended (L4/L5) execution is unavailable.
/// A no-grant-yet signature reports <see cref="Enums.AutonomyLevel.Approve"/> (L3, the permanent
/// human-approved floor every signature always stands on) — never a fabricated lower level.
/// </summary>
/// <param name="SignatureHash">The failure signature evaluated.</param>
/// <param name="ActionKind">The recovery action evaluated.</param>
/// <param name="CurrentLevel">The grant's current level, numeric (matches <see cref="Enums.AutonomyLevel"/>).</param>
/// <param name="LevelLabel">Human-readable level name, e.g. <c>"Approve (L3)"</c>, <c>"Standing (L4)"</c>.</param>
/// <param name="CanAutoReplay">Whether this signature is currently eligible for unattended
/// (L4/L5) auto-replay — mirrors the Eligibility Gate predicate 5 check exactly, including its
/// provider-capability corroboration.</param>
/// <param name="CanProveDlqAbsence">Whether the signature's own cloud provider can independently
/// verify DLQ absence — the real, structural fact that gates L4/L5 regardless of trust evidence.</param>
/// <param name="BlockedReason">A real, non-fabricated explanation for why unattended execution is
/// unavailable, populated only when <see cref="CanAutoReplay"/> is <see langword="false"/> and a
/// concrete reason is known (e.g. the provider's capability limitation); <see langword="null"/>
/// when auto-replay is available, or when no signature-specific reason can be determined.</param>
public sealed record SignatureAutonomyStatusResponse(
    string SignatureHash,
    string ActionKind,
    int CurrentLevel,
    string LevelLabel,
    bool CanAutoReplay,
    bool CanProveDlqAbsence,
    string? BlockedReason);

/// <summary>
/// Response DTO for <c>GET /api/v1/recovery/approval-queue</c> (roadmap §11 item 1) — one
/// auto-replay rule match the Eligibility Gate escalated for manual review, whose underlying DLQ
/// message is still <c>Active</c> (approvable). Carries everything the existing single-message
/// replay endpoint (<c>POST /api/v1/messages/replay</c>) needs, so "approve" is a plain call to
/// that already-gated, already-audited path — this queue adds no new execution path.
/// </summary>
/// <param name="EntryId">The <see cref="Entities.RecoveryLedgerEntry"/> that recorded the decline.</param>
/// <param name="NamespaceId">Namespace to replay into.</param>
/// <param name="NamespaceName">Namespace display name, for the panel.</param>
/// <param name="Provider">Cloud provider snapshot.</param>
/// <param name="Environment">Deployment environment snapshot.</param>
/// <param name="EntityName">Queue name, or topic name when <paramref name="SubscriptionName"/> is set — the exact value the replay endpoint's <c>entityName</c> parameter expects.</param>
/// <param name="SubscriptionName">Subscription name, when the entity is a topic subscription; null for a queue.</param>
/// <param name="SequenceNumber">The provider sequence number to replay — the replay endpoint's <c>sequenceNumber</c> parameter.</param>
/// <param name="FailureCategory">Heuristic failure category, snapshotted.</param>
/// <param name="RuleId">The <see cref="Entities.AutoReplayRule"/> whose match was escalated.</param>
/// <param name="RuleName">The rule's name at match time.</param>
/// <param name="ReasonCode">The Eligibility Gate predicate that escalated this attempt (e.g. <c>AUTONOMY_GRANT_INSUFFICIENT</c>).</param>
/// <param name="MatchedCount">Recurrence-lineage match count carried on the decline, when the reason relates to the recurrence cap; null otherwise.</param>
/// <param name="DeclinedAt">When the Eligibility Gate escalated this attempt.</param>
/// <param name="SignatureHash">
/// The failure signature this entry belongs to, when one was computed (see W1.5). Lets the
/// frontend enrich a static reason label (e.g. <c>AUTONOMY_GRANT_INSUFFICIENT</c>) with this
/// signature's actual trust evidence — "8 of 10 verified recoveries" instead of a generic
/// sentence — via <c>GET recovery/trust/{signatureHash}</c> (roadmap W2.5, §5.2).
/// </param>
public sealed record ApprovalQueueEntryResponse(
    Guid EntryId,
    Guid NamespaceId,
    string? NamespaceName,
    string? Provider,
    string? Environment,
    string EntityName,
    string? SubscriptionName,
    long SequenceNumber,
    string? FailureCategory,
    long RuleId,
    string RuleName,
    string? ReasonCode,
    int? MatchedCount,
    DateTimeOffset DeclinedAt,
    string? SignatureHash);

/// <summary>
/// Response DTO for rehearsal mode (roadmap §7 W1.2, <c>POST /api/v1/recovery/entries/{id}/rehearse</c>) —
/// what <see cref="Interfaces.IRecoveryEligibilityGate"/> would decide for this entry's recorded
/// identity, evaluated as of now. A read: nothing was executed, and nothing was recorded to the
/// ledger by this call.
/// </summary>
/// <param name="EntryId">The rehearsed <see cref="Entities.RecoveryLedgerEntry"/>.</param>
/// <param name="ActorKindEvaluated">Which <see cref="Enums.RecoveryActorKind"/> the gate was evaluated as.</param>
/// <param name="Verdict">The gate's verdict: <c>Allow</c>, <c>Escalate</c>, or <c>Deny</c>.</param>
/// <param name="ReasonCode">Which predicate produced the verdict, or carried recurrence-cap context on an <c>Allow</c>; null when no predicate fired.</param>
/// <param name="MatchedCount">Predicate 3 only: how many prior lineage-matched entries were found.</param>
/// <param name="EvaluatedAt">When this rehearsal ran.</param>
public sealed record RecoveryRehearsalResponse(
    Guid EntryId,
    string ActorKindEvaluated,
    string Verdict,
    string? ReasonCode,
    int MatchedCount,
    DateTimeOffset EvaluatedAt);
