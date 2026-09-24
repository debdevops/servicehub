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
