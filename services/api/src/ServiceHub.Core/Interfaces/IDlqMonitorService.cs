using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Service for monitoring dead-letter queues across all registered namespaces.
/// Detects new DLQ messages, computes deduplication hashes, and categorizes failures.
/// </summary>
public interface IDlqMonitorService
{
    /// <summary>
    /// Scans all DLQs in a namespace and persists any new messages found.
    /// </summary>
    /// <param name="namespaceId">The namespace to scan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of new messages detected and stored.</returns>
    Task<Result<int>> ScanNamespaceAsync(Guid namespaceId, CancellationToken cancellationToken = default);
}
