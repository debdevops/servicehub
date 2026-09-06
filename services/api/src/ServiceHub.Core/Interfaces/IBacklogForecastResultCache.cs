using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Durable store of computed <see cref="BacklogForecast"/> instances (roadmap next-chapter M1,
/// ADR-0009).
/// </summary>
/// <remarks>
/// Backed by <c>DlqDbContext.BacklogForecasts</c> as of M1 — before this, forecasts lived in a
/// process-local, 24-hour TTL cache and were lost on every restart. See
/// <c>SqliteBacklogForecastResultCache</c> and <c>PillarFindingRetentionWorker</c>.
/// </remarks>
public interface IBacklogForecastResultCache
{
    /// <summary>
    /// Stores (or refreshes) a batch of freshly computed forecasts, keyed by their
    /// <see cref="BacklogForecast.Id"/>.
    /// </summary>
    /// <param name="ownerId">The owner the forecasts were computed for.</param>
    /// <param name="forecasts">The forecasts to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(string ownerId, IEnumerable<BacklogForecast> forecasts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve a previously stored forecast by ID.
    /// </summary>
    /// <returns>The forecast, or <c>null</c> if it was never stored or has since been pruned by
    /// retention.</returns>
    Task<BacklogForecast?> TryGetAsync(Guid id, CancellationToken cancellationToken = default);
}
