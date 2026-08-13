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
    int TargetCount);

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
