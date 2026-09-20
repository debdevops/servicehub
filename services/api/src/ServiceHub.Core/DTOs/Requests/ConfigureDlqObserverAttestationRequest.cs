using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Configures (or updates) one namespace's DLQ observer attestation (ADR-004; ADR-0011) — an
/// operator action taken once the corresponding `cloud-platform-infra` observer module has
/// actually been applied for this namespace's DLQ. Configuring this does not itself make
/// <c>CanProveDlqAbsence</c> true; the liveness canary must confirm at least once first.
/// </summary>
/// <param name="Enabled">Whether attestation is active for this namespace.</param>
/// <param name="ObserverReference">The DynamoDB table name (AWS) or Firestore collection name
/// (GCP) the observer's log lives in. Required when <paramref name="Enabled"/> is true.</param>
/// <param name="DlqEntityName">The DLQ entity name the liveness canary is sent to. Required when
/// <paramref name="Enabled"/> is true.</param>
/// <param name="StalenessBoundMinutes">How old the last confirmation may be before liveness is
/// judged false. Defaults to 60.</param>
public sealed record ConfigureDlqObserverAttestationRequest(
    bool Enabled,
    [StringLength(256)]
    string? ObserverReference,

    [StringLength(256)]
    string? DlqEntityName,

    [Range(1, 1440, ErrorMessage = "StalenessBoundMinutes must be between 1 and 1440 (one day).")]
    int StalenessBoundMinutes = 60);
