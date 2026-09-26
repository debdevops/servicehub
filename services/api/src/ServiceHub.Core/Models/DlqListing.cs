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
/// <remarks>
/// <paramref name="ResolvedAt"/> and <paramref name="ResolutionCause"/> are what was recorded when it left the queue
/// (unit 6.11) — a scan that only saw it gone records <see cref="DlqResolutionCause.VanishedExternally"/>, never who removed it.
/// </remarks>
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
    DlqMessageStatus Status,
    DateTimeOffset? ResolvedAt = null,
    DlqResolutionCause? ResolutionCause = null);

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

/// <summary>
/// One stored dead letter opened for reading (unit 2.4): the row plus what the drawer needs to judge a
/// replay. The body is only the stored preview — the first 500 characters — and says so.
/// </summary>
/// <param name="Item">The same row the list shows.</param>
/// <param name="BodyPreview">The first 500 characters of the body, or null when none was stored.</param>
/// <param name="BodyIsPreview">True when the body is longer than what is held here.</param>
/// <param name="ContentType">Body content type.</param>
/// <param name="CorrelationId">The message's correlation id.</param>
/// <param name="SessionId">The message's session id.</param>
/// <param name="ApplicationPropertiesJson">The application properties as stored JSON, or null.</param>
/// <param name="ResolvedAt">When a scan found it gone, or null.</param>
/// <param name="OthersLikeIt">Other active dead letters in this queue with the same recorded reason.</param>
/// <param name="BodyHash">SHA-256 of the body. Server-side only (the eligibility gate's lineage key); never serialised.</param>
public sealed record DlqDetail(
    DlqListItem Item,
    string? BodyPreview,
    bool BodyIsPreview,
    string? ContentType,
    string? CorrelationId,
    string? SessionId,
    string? ApplicationPropertiesJson,
    DateTimeOffset? ResolvedAt,
    int OthersLikeIt,
    [property: System.Text.Json.Serialization.JsonIgnore] string BodyHash = "");

/// <summary>One day of the dead-letter trend: how many were first seen, and how many were seen to leave (unit 2.10).</summary>
/// <param name="Date">The UTC day, <c>yyyy-MM-dd</c>.</param>
/// <param name="New">Dead letters first seen that day.</param>
/// <param name="Resolved">Dead letters that a scan (or a replay) found gone that day.</param>
public sealed record DlqTrendDay(string Date, int New, int Resolved);
