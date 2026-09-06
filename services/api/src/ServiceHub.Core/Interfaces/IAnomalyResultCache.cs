using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Durable store of detected <see cref="Anomaly"/> instances (roadmap next-chapter M1, ADR-0009).
/// </summary>
/// <remarks>
/// Backed by <c>DlqDbContext.Anomalies</c> as of M1 — before this, findings lived in a
/// process-local, 24-hour TTL cache and were lost on every restart, which meant
/// <c>GET /v1/anomalies/{id}</c> and any evidence citing an anomaly stopped resolving within a
/// day. See <c>SqliteAnomalyResultCache</c> and <c>PillarFindingRetentionWorker</c> (which prunes
/// entries older than a configurable window, except any a <c>PlaybookEntry</c> still cites).
/// </remarks>
public interface IAnomalyResultCache
{
    /// <summary>
    /// Stores (or refreshes) a batch of freshly detected anomalies, keyed by their
    /// <see cref="Anomaly.Id"/>.
    /// </summary>
    /// <param name="ownerId">The owner the anomalies were detected for.</param>
    /// <param name="anomalies">The anomalies to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(string ownerId, IEnumerable<Anomaly> anomalies, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve a previously stored anomaly by ID.
    /// </summary>
    /// <returns>The anomaly, or <c>null</c> if it was never stored or has since been pruned by
    /// retention.</returns>
    Task<Anomaly?> TryGetAsync(Guid id, CancellationToken cancellationToken = default);
}
