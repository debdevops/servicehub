namespace ServiceHub.Core.Models;

/// <summary>
/// A P5 <c>PreventionTrigger</c> proposal's structured content
/// (<c>PREVENTION-RULE-DESIGN-2026-08-29.md</c> §5/§11) — the opaque payload behind a
/// <c>PlaybookEntry</c> with <c>ProposalKind = "PreventionTrigger"</c>. Pure evidence that a
/// promoted <see cref="PreventionRuleProposal"/>'s condition matched a fresh
/// <see cref="Entities.DriftFinding"/> — never itself a decision request (§12): nothing reads this
/// back to authorize anything, and <c>BacktestService</c> joins on <see cref="EntityName"/>
/// the same way it already does for every other backtestable <c>ProposalKind</c>.
/// </summary>
public sealed record PreventionTriggerProposal(
    Guid RuleLineageId,
    Guid RuleEntryId,
    int RuleVersion,
    string Name,
    string EntityName,
    Guid DriftFindingId,
    string FindingType,
    int FindingSeverity,
    int OccurrencesInWindow,
    int MinOccurrences,
    int WindowHours,
    bool MetOccurrenceThreshold);
