namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// A single "hop" in a cross-cloud message trace — one occurrence of the traced message
/// found in a specific cloud namespace/entity.
/// </summary>
/// <param name="CloudProvider">The cloud where this hop was found: "azure", "aws", or "gcp".</param>
/// <param name="NamespaceId">The namespace ID.</param>
/// <param name="NamespaceDisplayName">Human-readable namespace name for UI rendering.</param>
/// <param name="EntityName">The queue, subscription, or topic name.</param>
/// <param name="EntityPath">Full entity path (e.g., topic/subscriptions/sub).</param>
/// <param name="MessageId">The message ID as seen in this cloud's messaging system.</param>
/// <param name="SequenceNumber">The sequence/offset number in this cloud's system.</param>
/// <param name="State">The message state: Active, Scheduled, Deferred, DeadLettered, Replayed, Resolved.</param>
/// <param name="Timestamp">UTC time when this event was enqueued / detected.</param>
/// <param name="DeadLetterReason">Dead-letter reason if applicable.</param>
/// <param name="BodyPreview">First 200 characters of the message body.</param>
/// <param name="SizeInBytes">Message size.</param>
/// <param name="Source">"Live" (from live queue peek) or "History" (from SQLite DLQ intelligence).</param>
/// <param name="HopIndex">0-based position in the chronological trace (0 = first seen).</param>
public sealed record CrossCloudTraceHop(
    string CloudProvider,
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
    long SizeInBytes,
    string Source,
    int HopIndex
);

/// <summary>
/// Summary of one namespace's contribution to the cross-cloud trace search.
/// </summary>
/// <param name="NamespaceId">The namespace ID.</param>
/// <param name="NamespaceDisplayName">Human-readable namespace name.</param>
/// <param name="CloudProvider">Cloud provider: "azure", "aws", or "gcp".</param>
/// <param name="WasSearched">Whether this namespace was actually searched (false when Phase 2).</param>
/// <param name="SkipReason">Why this namespace was skipped, if applicable (e.g., "AWS SDK Phase 2").</param>
/// <param name="HopsFound">Number of matching hops found in this namespace.</param>
public sealed record CrossCloudNamespaceSummary(
    Guid NamespaceId,
    string NamespaceDisplayName,
    string CloudProvider,
    bool WasSearched,
    string? SkipReason,
    int HopsFound
);

/// <summary>
/// Complete response for a cross-cloud message trace search.
/// </summary>
/// <param name="TraceId">The correlation/trace ID that was searched.</param>
/// <param name="Hops">All trace hops sorted chronologically (oldest first).</param>
/// <param name="NamespaceSummaries">Per-namespace search summaries.</param>
/// <param name="TotalHops">Total number of hops found.</param>
/// <param name="CloudsInvolved">Number of distinct clouds that had at least one hop.</param>
/// <param name="CloudProviders">List of distinct cloud providers that had hops (e.g. ["azure", "gcp"]).</param>
/// <param name="IsMultiCloud">True when hops span 2 or more cloud providers.</param>
/// <param name="NamespacesSearched">Total namespaces searched (excludes Phase 2 skips).</param>
/// <param name="EntitiesSearched">Total queue/topic/subscription entities searched.</param>
/// <param name="IsPartialResult">True when the search timed out before completion.</param>
/// <param name="SearchDurationMs">Wall-clock time the search took in milliseconds.</param>
public sealed record CrossCloudTraceResponse(
    string TraceId,
    IReadOnlyList<CrossCloudTraceHop> Hops,
    IReadOnlyList<CrossCloudNamespaceSummary> NamespaceSummaries,
    int TotalHops,
    int CloudsInvolved,
    IReadOnlyList<string> CloudProviders,
    bool IsMultiCloud,
    int NamespacesSearched,
    int EntitiesSearched,
    bool IsPartialResult,
    long SearchDurationMs
);
