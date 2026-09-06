using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Durable store of detected <see cref="ExternalSignalCorrelation"/> instances (roadmap
/// next-chapter M1, ADR-0009).
/// </summary>
/// <remarks>
/// Backed by <c>DlqDbContext.ExternalSignalCorrelations</c> as of M1 — before this, correlations
/// lived in a process-local, 24-hour TTL cache and were lost on every restart. The raw signal
/// (<see cref="ExternalSignalEvent"/>) was already durable (M5); this closes the gap for the
/// correlation hypothesis derived from it. See <c>SqliteExternalSignalCorrelationCache</c> and
/// <c>PillarFindingRetentionWorker</c>.
/// </remarks>
public interface IExternalSignalCorrelationCache
{
    /// <summary>
    /// Stores (or refreshes) a batch of freshly detected correlations, keyed by their
    /// <see cref="ExternalSignalCorrelation.Id"/>. <see cref="ExternalSignalCorrelation.OwnerId"/>
    /// is already carried on the entity, so no separate owner parameter is needed here.
    /// </summary>
    /// <param name="correlations">The correlations to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(IEnumerable<ExternalSignalCorrelation> correlations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve a previously stored correlation by ID.
    /// </summary>
    /// <returns>The correlation, or <c>null</c> if it was never stored or has since been pruned by
    /// retention.</returns>
    Task<ExternalSignalCorrelation?> TryGetAsync(Guid id, CancellationToken cancellationToken = default);
}
