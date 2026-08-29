namespace ServiceHub.Core.Models;

/// <summary>
/// The only legal value of <see cref="PreventionRuleProposal.Action"/> in this design
/// (<c>PREVENTION-RULE-DESIGN-2026-08-29.md</c> §3) — validated server-side on every propose/
/// revise call, so there is no code path, at any promotion level, that lets a <c>PreventionRule</c>
/// carry execution authority. Never accepted from a caller; every writer in this codebase hard-codes
/// it, which is strictly safer than accepting-then-validating a client-supplied value.
/// </summary>
public static class PreventionRuleActions
{
    public const string ObserveOnly = "ObserveOnly";
}

/// <summary>
/// A P5 <c>PreventionRule</c> proposal's structured content
/// (<c>PREVENTION-RULE-DESIGN-2026-08-29.md</c> §3/§8) — the opaque payload behind a
/// <c>PlaybookEntry</c> with <c>ProposalKind = "PreventionRuleProposal"</c>. Opaque to the ledger
/// itself; only this pillar's own writer/readers agree on this shape (the same "ledger stays
/// opaque" split every other <c>ProposalKind</c> already uses).
/// </summary>
/// <param name="RuleLineageId">Stable across every version of the same rule — minted once, at the
/// very first proposal — and never itself a column; it is what lets "the currently active version"
/// be a derived read (§8) instead of a new table.</param>
/// <param name="RuleVersion">1 for a brand-new rule, incremented on every edit.</param>
/// <param name="Name">Human-readable label.</param>
/// <param name="EntityName">The single entity (queue/topic/subscription) this rule is scoped to —
/// read the same way <c>BacktestService.ExtractEntityName</c> already reads every other
/// <c>ProposalKind</c>'s <c>EntityName</c> field.</param>
/// <param name="Condition">What has to be true of a fresh <see cref="Entities.DriftFinding"/> for
/// this rule to match it.</param>
/// <param name="SupersedesRuleEntryId">The prior version's <c>PlaybookEntry.Id</c>, when this
/// proposal is an edit of an already-promoted rule; <see langword="null"/> for a brand-new rule.</param>
/// <param name="RuleExpiresAt">When a promoted rule lapses without reconfirmation (§9) — distinct
/// from the entry's own <c>ExpiresAt</c> column, which stops mattering once the entry reaches the
/// terminal <c>Approved</c> state.</param>
/// <param name="Action">Always <see cref="PreventionRuleActions.ObserveOnly"/> in this design.</param>
public sealed record PreventionRuleProposal(
    Guid RuleLineageId,
    int RuleVersion,
    string Name,
    string EntityName,
    PreventionRuleCondition Condition,
    Guid? SupersedesRuleEntryId,
    DateTimeOffset RuleExpiresAt,
    string Action);
