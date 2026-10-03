namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// One message as peeked from a cloud, live. Nothing here is stored: ServiceHub never persists a
/// message body (ADR-0004) — it is read from the cloud when asked for and returned.
/// </summary>
/// <param name="MessageId">The broker's message id.</param>
/// <param name="SequenceNumber">
/// Azure's broker-assigned sequence number. On AWS and Google it is a stable hash of the message id: it
/// identifies the message but says nothing about order, and cannot be used to page.
/// </param>
/// <param name="Body">The body as text, or null when there is none.</param>
/// <param name="ContentType">Body content type.</param>
/// <param name="CorrelationId">The correlation id, if any.</param>
/// <param name="SessionId">The session id, if any.</param>
/// <param name="Subject">The subject or label, if any.</param>
/// <param name="EnqueuedTime">When it was enqueued.</param>
/// <param name="DeliveryCount">How many times delivery was attempted.</param>
/// <param name="DeadLetterReason">Why it was dead-lettered, when it was.</param>
/// <param name="DeadLetterErrorDescription">The error description that came with it.</param>
/// <param name="ApplicationProperties">Application properties, or null.</param>
/// <param name="SizeInBytes">Size on the wire.</param>
/// <param name="IsFromDeadLetter">Whether it was peeked from a dead-letter queue.</param>
public sealed record MessageResponse(
    string MessageId,
    long SequenceNumber,
    string? Body,
    string? ContentType,
    string? CorrelationId,
    string? SessionId,
    string? Subject,
    DateTimeOffset EnqueuedTime,
    int DeliveryCount,
    string? DeadLetterReason,
    string? DeadLetterErrorDescription,
    IReadOnlyDictionary<string, object>? ApplicationProperties,
    long SizeInBytes,
    bool IsFromDeadLetter);

/// <summary>How a peek was paged.</summary>
/// <param name="Requested">How many were asked for (1–100).</param>
/// <param name="Returned">How many came back.</param>
/// <param name="NextFromSequenceNumber">
/// Pass this as <c>from</c> for the next page. Null when there is no cursor — either the page was not
/// full, or the cloud cannot page a peek at all (see <see cref="PeekSafety.Repeatable"/>).
/// </param>
public sealed record PeekPaging(int Requested, int Returned, long? NextFromSequenceNumber);

/// <summary>What looking at messages does to this cloud — read from capabilities, never from its name (R4).</summary>
/// <param name="Repeatable">
/// True when peeking changes nothing and may be repeated or refreshed. False when every peek is a real
/// receive that counts as a delivery attempt: a caller must not poll, and must say so to the person.
/// </param>
/// <param name="Warning">One sentence for a person when <paramref name="Repeatable"/> is false; otherwise null.</param>
public sealed record PeekSafety(bool Repeatable, string? Warning);

/// <summary>A page of peeked messages.</summary>
/// <param name="NamespaceId">The namespace.</param>
/// <param name="Entity">The queue or topic asked about.</param>
/// <param name="Subscription">The subscription, when the entity is a topic.</param>
/// <param name="DeadLetter">Whether this is the dead-letter side.</param>
/// <param name="Messages">The messages.</param>
/// <param name="Paging">Paging.</param>
/// <param name="Peek">What peeking does to this cloud.</param>
public sealed record PeekResponse(
    Guid NamespaceId,
    string Entity,
    string? Subscription,
    bool DeadLetter,
    IReadOnlyList<MessageResponse> Messages,
    PeekPaging Paging,
    PeekSafety Peek);
