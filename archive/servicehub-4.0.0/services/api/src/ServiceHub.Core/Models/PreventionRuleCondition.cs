namespace ServiceHub.Core.Models;

/// <summary>
/// The condition half of a P5 <c>PreventionRuleProposal</c> — a narrower filter over the P1/P2
/// drift signal for one entity (<c>PREVENTION-RULE-DESIGN-2026-08-29.md</c> §3/§4). Lives inside
/// <see cref="PreventionRuleProposal.Condition"/>, itself serialized into the opaque
/// <c>PlaybookEntry.ProposalJson</c> — never a new column.
/// </summary>
/// <param name="DriftFindingType">Either the literal <c>"Any"</c>, or the exact name of a
/// <see cref="Enums.DriftFindingType"/> member (e.g. <c>"SchemaShapeDrift"</c>) — validated
/// server-side before the proposal is written.</param>
/// <param name="MinSeverity">Minimum <see cref="Entities.DriftFinding.Severity"/> (0-100) a fresh
/// finding must meet to match.</param>
/// <param name="MinOccurrences">How many matches within <see cref="WindowHours"/> a human
/// considers this pattern actionable. Never a write-suppression gate (every match is still
/// recorded as a <c>PreventionTrigger</c> — see the evaluation service) — purely descriptive
/// metadata a reviewer or backtest can filter on, avoiding the circularity of gating a write on a
/// count that write itself would have to contribute to.</param>
/// <param name="WindowHours">The rolling window <see cref="MinOccurrences"/> is counted over.</param>
public sealed record PreventionRuleCondition(
    string DriftFindingType,
    int MinSeverity,
    int MinOccurrences,
    int WindowHours);
