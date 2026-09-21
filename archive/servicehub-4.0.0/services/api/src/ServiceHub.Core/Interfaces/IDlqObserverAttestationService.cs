using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// The sole reader and writer of <see cref="DlqObserverAttestation"/> rows — per-namespace
/// liveness state for the infrastructure-attested DLQ observer (`cloud-platform-infra` ADR-004;
/// ADR-0011). Never itself decides <c>CanProveDlqAbsence</c>; callers (the Recovery Eligibility
/// Gate, <c>AutonomyEvaluationWorker</c>) combine <see cref="IsLiveAsync"/> with the provider's
/// static <see cref="Models.ProviderCapabilities"/> default.
/// </summary>
public interface IDlqObserverAttestationService
{
    /// <summary>Gets one namespace's attestation row, or <see langword="null"/> if none exists
    /// (attestation was never configured for it).</summary>
    Task<DlqObserverAttestation?> GetAsync(
        string ownerId, Guid namespaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether <paramref name="namespaceId"/>'s observer is currently live — enabled and
    /// confirmed within its staleness bound. Fail-closed: returns <see langword="false"/> when no
    /// row exists at all, exactly as it does for a row that exists but has never confirmed or has
    /// gone stale.
    /// </summary>
    Task<bool> IsLiveAsync(
        string ownerId, Guid namespaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates or updates a namespace's attestation configuration — an operator action taken
    /// once the corresponding `cloud-platform-infra` observer module has actually been applied.
    /// Setting <paramref name="enabled"/> to <see langword="false"/> does not clear
    /// <see cref="DlqObserverAttestation.LastConfirmedAt"/>; <see cref="IsLiveAsync"/> is false
    /// while disabled regardless, and re-enabling does not resurrect a stale confirmation.
    /// </summary>
    Task<Result<DlqObserverAttestation>> ConfigureAsync(
        string ownerId,
        Guid namespaceId,
        bool enabled,
        string? observerReference,
        string? dlqEntityName,
        int stalenessBoundMinutes,
        CancellationToken cancellationToken = default);

    /// <summary>Records that a liveness canary was just dispatched to
    /// <see cref="DlqObserverAttestation.DlqEntityName"/> — sets
    /// <see cref="DlqObserverAttestation.LastCanarySentAt"/>/<see cref="DlqObserverAttestation.LastCanaryMessageId"/>.
    /// Does not itself change <see cref="DlqObserverAttestation.LastConfirmedAt"/>.</summary>
    Task<Result<DlqObserverAttestation>> RecordCanarySentAsync(
        string ownerId, Guid namespaceId, string canaryMessageId, CancellationToken cancellationToken = default);

    /// <summary>Records that the observer's log confirmed the most recently sent canary's
    /// arrival — sets <see cref="DlqObserverAttestation.LastConfirmedAt"/> to now.</summary>
    Task<Result<DlqObserverAttestation>> RecordCanaryConfirmedAsync(
        string ownerId, Guid namespaceId, CancellationToken cancellationToken = default);

    /// <summary>Every attestation row with <see cref="DlqObserverAttestation.Enabled"/> true,
    /// across every owner — <c>DlqObserverAttestationWorker</c>'s sweep set.</summary>
    Task<IReadOnlyList<DlqObserverAttestation>> GetAllEnabledAsync(
        CancellationToken cancellationToken = default);
}
