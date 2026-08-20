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
