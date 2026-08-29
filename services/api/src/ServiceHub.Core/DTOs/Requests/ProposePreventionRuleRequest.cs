namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Request body for <c>POST /api/v1/prevention-rules</c> — proposes a new P5 <c>PreventionRule</c>,
/// or a new version of an existing one when <see cref="SupersedesRuleEntryId"/> is set
/// (<c>PREVENTION-RULE-DESIGN-2026-08-29.md</c> §3/§8). There is no <c>Action</c> field: the
/// controller always writes <c>PreventionRuleActions.ObserveOnly</c> — accepting one from the
/// caller would be a policy a future editor could quietly loosen, exactly what §3 rules out.
/// </summary>
/// <param name="Name">Human-readable label.</param>
/// <param name="NamespaceId">The namespace this rule is scoped to — required, unlike a fleet-wide
/// correlation hypothesis (§7).</param>
/// <param name="EntityName">The single queue/topic/subscription this rule watches.</param>
/// <param name="DriftFindingType">Either the literal <c>"Any"</c>, or the exact name of a
/// <see cref="ServiceHub.Core.Enums.DriftFindingType"/> member.</param>
/// <param name="MinSeverity">Minimum finding severity (0-100) that matches.</param>
/// <param name="MinOccurrences">Descriptive corroboration threshold — see
/// <see cref="ServiceHub.Core.Models.PreventionRuleCondition.MinOccurrences"/>. Defaults to 1
/// (every match counts).</param>
/// <param name="WindowHours">The rolling window <see cref="MinOccurrences"/> is counted over.</param>
/// <param name="RuleExpiresAt">When this rule lapses without reconfirmation — must be in the future.</param>
/// <param name="SupersedesRuleEntryId">The prior version's <c>PlaybookEntry.Id</c>, when this is an
/// edit of an already-promoted rule; <see langword="null"/> for a brand-new rule.</param>
/// <param name="Justification">Optional free-text note on why this rule is being proposed —
/// carried as the proposal's evidence reference. Never a message body or credential (redacted the
/// same way every other Playbook Ledger write is).</param>
public sealed record ProposePreventionRuleRequest(
    string Name,
    Guid NamespaceId,
    string EntityName,
    string DriftFindingType,
    int MinSeverity,
    int MinOccurrences,
    int WindowHours,
    DateTimeOffset RuleExpiresAt,
    Guid? SupersedesRuleEntryId,
    string? Justification);
