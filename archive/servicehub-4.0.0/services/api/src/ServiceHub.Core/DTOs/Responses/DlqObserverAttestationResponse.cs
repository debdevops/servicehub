namespace ServiceHub.Core.DTOs.Responses;

/// <summary>Response shape for a <c>DlqObserverAttestation</c> row. <see cref="IsLive"/> is the
/// single fact the Recovery Eligibility Gate and <c>AutonomyEvaluationWorker</c> evaluate.</summary>
public sealed record DlqObserverAttestationResponse(
    Guid NamespaceId,
    bool Enabled,
    string? ObserverReference,
    string? DlqEntityName,
    DateTimeOffset? LastCanarySentAt,
    DateTimeOffset? LastConfirmedAt,
    int StalenessBoundMinutes,
    bool IsLive);
