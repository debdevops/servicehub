namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Request body for the emergency-stop activate/clear endpoints (roadmap §9.4.2, §15.2).</summary>
/// <param name="Reason">Optional administrator justification, carried as forensic detail on the
/// resulting <c>EmergencyStopActivated</c>/<c>EmergencyStopCleared</c> ledger event.</param>
public sealed record EmergencyStopRequest(string? Reason);
