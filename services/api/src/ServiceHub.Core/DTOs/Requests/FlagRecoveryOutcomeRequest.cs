using System.ComponentModel.DataAnnotations;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Request body for <c>POST /api/v1/recovery/entries/{id}/outcome-flag</c>.</summary>
/// <param name="FlagKind">Which operator attestation this is — <c>Unsafe</c> or
/// <c>DuplicateBusinessEffect</c> (roadmap §8.10).</param>
/// <param name="Reason">Mandatory operator justification for the attestation.</param>
public sealed record FlagRecoveryOutcomeRequest(
    RecoveryOutcomeFlagKind FlagKind,
    [Required(ErrorMessage = "Reason is required to flag a recovery outcome")]
    [MinLength(1)]
    string Reason);
