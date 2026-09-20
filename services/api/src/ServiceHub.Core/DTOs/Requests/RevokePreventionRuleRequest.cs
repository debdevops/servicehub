namespace ServiceHub.Core.DTOs.Requests;

/// <summary>Request body for <c>POST /api/v1/prevention-rules/{id}/revoke</c> — always requires a
/// reason, enforced authoritatively by <c>IPlaybookLedger.RevokeAsync</c> itself, not this DTO,
/// the same way <c>DispositionPlaybookEntryRequest</c>'s rejection reason is.</summary>
public sealed record RevokePreventionRuleRequest(string Reason);
