using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;

namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// A page of the dead letters ServiceHub has seen — the durable list, not a live peek (a peek is
/// <c>messages/peek</c>). It carries no message body.
/// </summary>
/// <param name="Items">The page, newest first.</param>
/// <param name="Paging">Where this page sits.</param>
/// <param name="Groups">Recorded reasons and how many messages have each, over the set the filters leave — so they add up.</param>
/// <param name="OtherReasons">Reasons too rare to show as their own chip, or null.</param>
/// <param name="Entities">The queues and subscriptions that have dead letters, for the filter.</param>
public sealed record DeadLetterListResponse(
    IReadOnlyList<DlqListItem> Items,
    DeadLetterPaging Paging,
    IReadOnlyList<DlqReasonGroup> Groups,
    DlqReasonGroupOther? OtherReasons,
    IReadOnlyList<string> Entities);

/// <summary>Paging over the filtered set.</summary>
/// <param name="Total">Every message matching every filter.</param>
/// <param name="Page">1-based.</param>
/// <param name="PageSize">1–100.</param>
public sealed record DeadLetterPaging(int Total, int Page, int PageSize);

/// <summary>
/// What the eligibility gate says about acting on one dead letter, with the reason as a <b>code</b>
/// (<c>PROVIDER_CANNOT_VERIFY_ABSENCE</c>, <c>PRODUCTION_ELEVATION_REQUIRED</c>…) — never flattened to prose,
/// so the code can carry its remedy (unit 2.6).
/// </summary>
/// <param name="Action">The action asked about: <c>replay</c> or <c>purge</c>.</param>
/// <param name="Verdict">Allow, Escalate or Deny.</param>
/// <param name="ReasonCode">The predicate's named code; null on a clean Allow.</param>
/// <param name="MatchedCount">Prior attempts on this lineage, when the recurrence check found any.</param>
/// <param name="Approvable">
/// True only for Escalate: a human can approve past it. Deny cannot be approved — the two are never the same.
/// </param>
public sealed record EligibilityResponse(string Action, string Verdict, string? ReasonCode, int MatchedCount, bool Approvable);

/// <summary>The dead-letter trend: one entry per day, oldest first, every day present.</summary>
/// <param name="Days">How many days were asked for (1–30).</param>
/// <param name="Series">New and resolved per day.</param>
public sealed record DeadLetterTrendResponse(int Days, IReadOnlyList<DlqTrendDay> Series);
