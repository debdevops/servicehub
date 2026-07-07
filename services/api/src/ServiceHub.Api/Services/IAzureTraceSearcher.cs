using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;

namespace ServiceHub.Api.Services;

/// <summary>
/// Result of searching a single Azure Service Bus namespace for a trace/correlation ID.
/// </summary>
/// <param name="Hops">Matching message occurrences found in this namespace.</param>
/// <param name="Summary">Per-namespace search summary (searched / skipped + reason).</param>
/// <param name="EntitiesSearched">Number of entity surfaces peeked (active + DLQ).</param>
/// <param name="IsPartial">True when the search was cut short (e.g. timed out).</param>
public sealed record AzureTraceSearchResult(
    IReadOnlyList<CrossCloudTraceHop> Hops,
    CrossCloudNamespaceSummary Summary,
    int EntitiesSearched,
    bool IsPartial);

/// <summary>
/// Encapsulates the Azure Service Bus-specific logic for a cross-cloud trace: decrypting the
/// namespace connection string, resolving a cached client, and peeking queues and topic
/// subscriptions (both active and dead-letter) for a message carrying the given correlation ID.
/// Extracted from <c>CrossCloudTraceController</c> so the controller holds only orchestration,
/// not provider/client-cache logic.
/// </summary>
public interface IAzureTraceSearcher
{
    /// <summary>
    /// Searches a single Azure namespace for messages matching <paramref name="traceId"/>.
    /// </summary>
    /// <param name="ns">The Azure namespace to search.</param>
    /// <param name="traceId">The correlation/trace ID to match on.</param>
    /// <param name="searchToken">Cancellation token bounding the overall search.</param>
    Task<AzureTraceSearchResult> SearchAsync(Namespace ns, string traceId, CancellationToken searchToken);
}
