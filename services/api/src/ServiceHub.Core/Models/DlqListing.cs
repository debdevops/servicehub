using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>What to list from the durable dead-letter table (unit 2.3).</summary>
/// <param name="NamespaceIds">Only these namespaces. The caller has already decided which ones it may see.</param>
/// <param name="Status">Which lifecycle state — Active for the Dead letters tab; null for every state.</param>
/// <param name="Since">Only messages first seen at or after this moment; null for all time.</param>
/// <param name="Reason">Only this recorded dead-letter reason.</param>
/// <param name="NoReason">Only messages with no recorded reason. Mutually exclusive with <paramref name="Reason"/>.</param>
/// <param name="Entity">Only this queue or subscription.</param>
/// <param name="Search">Text found in the message id, the entity name or the recorded reason — never the body.</param>
/// <param name="Page">1-based.</param>
/// <param name="PageSize">1–100.</param>
public sealed record DlqListQuery(
    IReadOnlyCollection<Guid> NamespaceIds,
    DlqMessageStatus? Status = DlqMessageStatus.Active,
    DateTimeOffset? Since = null,
    string? Reason = null,
    bool NoReason = false,
    string? Entity = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 25);

/// <summary>One stored dead-letter row, as a list shows it. No body: a list is not the place for one.</summary>
public sealed record DlqListItem(
    long Id,
    Guid NamespaceId,
    string MessageId,
    long SequenceNumber,
    string EntityName,
    ServiceBusEntityType EntityType,
    string? TopicName,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset EnqueuedTimeUtc,
    int DeliveryCount,
    long SizeInBytes,
    string? DeadLetterReason,
    string? DeadLetterErrorDescription,
    DlqMessageStatus Status);

/// <summary>How many messages share one recorded reason.</summary>
/// <param name="Reason">The reason the cloud or the application recorded; null when none was.</param>
/// <param name="Count">How many.</param>
public sealed record DlqReasonGroup(string? Reason, int Count);

/// <summary>A page of dead letters and the shape of the set it came from.</summary>
/// <param name="Items">The page, newest first.</param>
/// <param name="Total">Every message matching every filter — what the pager counts.</param>
/// <param name="Page">1-based.</param>
/// <param name="PageSize">1–100.</param>
/// <param name="Groups">
/// Reasons over the messages matching every filter <i>except</i> the reason itself, largest first, so the
/// chips always add up and choosing one never hides the others.
/// </param>
/// <param name="OtherReasons">Reasons beyond the chips shown: how many messages and how many kinds. Null when none.</param>
/// <param name="Entities">The queues and subscriptions that have any, for the filter.</param>
public sealed record DlqPage(
    IReadOnlyList<DlqListItem> Items,
    int Total,
    int Page,
    int PageSize,
    IReadOnlyList<DlqReasonGroup> Groups,
    DlqReasonGroupOther? OtherReasons,
    IReadOnlyList<string> Entities);

/// <summary>The reasons too rare to be a chip of their own.</summary>
/// <param name="Count">How many messages.</param>
/// <param name="Kinds">How many distinct reasons.</param>
public sealed record DlqReasonGroupOther(int Count, int Kinds);
