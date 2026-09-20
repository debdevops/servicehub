using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Requests a time-boxed production elevation for one Prod namespace (ADR-0010 §Decision phase 2).
/// Grants nothing by itself — the caller must separately hold
/// <see cref="Enums.GovernanceRole.Operator"/> on <see cref="NamespaceId"/>, and a distinct
/// identity must approve before the elevation is live.
/// </summary>
/// <param name="NamespaceId">The Prod namespace to request access to.</param>
/// <param name="Reason">The stated reason for requesting production access. Required.</param>
/// <param name="DurationMinutes">
/// How long the elevation should stay live once approved, in minutes — applied at approval time,
/// not request time. Defaults to 480 (one shift). Clamped to [15, 1440] (one day): an elevation is
/// short by design (ADR-0010: "a shift, not a sprint") and cannot be extended once approved.
/// </param>
public sealed record RequestProductionElevationRequest(
    [Required]
    Guid NamespaceId,

    [Required(ErrorMessage = "A reason is required to request production access.")]
    [StringLength(1000, MinimumLength = 1)]
    string Reason,

    [Range(15, 1440, ErrorMessage = "DurationMinutes must be between 15 and 1440 (one day).")]
    int DurationMinutes = 480);
