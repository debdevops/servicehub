namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// A single event in the correlation timeline for a given CorrelationId.
/// </summary>
/// <param name="Source">The source of this entry: "Live" (from Service Bus peek) or "History" (from SQLite DLQ).</param>
/// <param name="NamespaceId">The namespace ID this message belongs to.</param>
/// <param name="NamespaceDisplayName">The namespace display name for UI rendering.</param>
/// <param name="EntityName">The queue or subscription name.</param>
/// <param name="EntityPath">The full entity path (e.g., topic/subscriptions/sub).</param>
/// <param name="MessageId">The Service Bus message ID.</param>
/// <param name="SequenceNumber">The sequence number.</param>
/// <param name="State">The message state: Active, Scheduled, Deferred, DeadLettered, Replayed, Resolved.</param>
/// <param name="Timestamp">When this event occurred (enqueued time for live, detectedAt for history).</param>
/// <param name="DeadLetterReason">Dead-letter reason if applicable.</param>
/// <param name="BodyPreview">Short body preview (first 200 chars).</param>
/// <param name="SizeInBytes">Size of the message in bytes.</param>
public sealed record CorrelationTimelineEntry(
    string Source,
    Guid NamespaceId,
    string NamespaceDisplayName,
    string EntityName,
    string? EntityPath,
    string MessageId,
    long SequenceNumber,
    string State,
    DateTimeOffset Timestamp,
    string? DeadLetterReason,
    string? BodyPreview,
    long SizeInBytes
);

/// <summary>
/// Complete correlation timeline response.
/// </summary>
/// <param name="CorrelationId">The correlation ID that was searched.</param>
/// <param name="Entries">All timeline entries sorted by Timestamp ascending.</param>
/// <param name="TotalCount">Total number of entries found.</param>
/// <param name="NamespacesSearched">Number of namespaces searched.</param>
/// <param name="EntitiesSearched">Number of entities (queues + subscriptions) searched.</param>
/// <param name="IsPartialResult">Whether the search timed out before completing.</param>
/// <param name="SearchDurationMs">How long the search took in milliseconds.</param>
public sealed record CorrelationTimelineResponse(
    string CorrelationId,
    IReadOnlyList<CorrelationTimelineEntry> Entries,
    int TotalCount,
    int NamespacesSearched,
    int EntitiesSearched,
    bool IsPartialResult,
    long SearchDurationMs
);
