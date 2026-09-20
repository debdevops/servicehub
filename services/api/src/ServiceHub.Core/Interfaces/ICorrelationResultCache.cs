using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Durable store of detected <see cref="CorrelationFinding"/> instances (roadmap next-chapter M1,
/// ADR-0009).
/// </summary>
/// <remarks>
/// Backed by <c>DlqDbContext.CorrelationFindings</c> as of M1 — before this, findings lived in a
/// process-local, 24-hour TTL cache and were lost on every restart. See
/// <c>SqliteCorrelationResultCache</c> and <c>PillarFindingRetentionWorker</c>.
/// </remarks>
public interface ICorrelationResultCache
{
    /// <summary>
    /// Stores (or refreshes) a batch of freshly detected correlation findings, keyed by their
    /// <see cref="CorrelationFinding.Id"/>. <see cref="CorrelationFinding.OwnerId"/> is already
    /// carried on the entity, so no separate owner parameter is needed here.
    /// </summary>
    /// <param name="findings">The findings to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(IEnumerable<CorrelationFinding> findings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve a previously stored correlation finding by ID.
    /// </summary>
    /// <returns>The finding, or <c>null</c> if it was never stored or has since been pruned by
    /// retention.</returns>
    Task<CorrelationFinding?> TryGetAsync(Guid id, CancellationToken cancellationToken = default);
}
