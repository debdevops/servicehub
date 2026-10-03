using System.ComponentModel.DataAnnotations;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Revokes a live or pending production elevation early. There is deliberately no
/// "renew" or "extend" request — a longer window means a new elevation with a new approval.</summary>
/// <param name="Reason">Optional explanation for the early revocation.</param>
public sealed record RevokeProductionElevationRequest(
    [StringLength(1000)]
    string? Reason = null);
