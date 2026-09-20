using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Durable store of generated <see cref="Narration"/> instances (roadmap next-chapter M1,
/// ADR-0009).
/// </summary>
/// <remarks>
/// Backed by <c>DlqDbContext.Narrations</c> as of M1 — before this, narrations lived in a
/// process-local, 24-hour TTL cache and were lost on every restart. See
/// <c>SqliteNarrationResultCache</c> and <c>PillarFindingRetentionWorker</c>.
/// </remarks>
public interface INarrationResultCache
{
    /// <summary>
    /// Stores (or refreshes) a batch of freshly generated narrations, keyed by their
    /// <see cref="Narration.Id"/>.
    /// </summary>
    /// <param name="narrations">The narrations to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(IEnumerable<Narration> narrations, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve a previously stored narration by ID.
    /// </summary>
    /// <returns>The narration, or <c>null</c> if it was never stored or has since been pruned by
    /// retention.</returns>
    Task<Narration?> TryGetAsync(Guid id, CancellationToken cancellationToken = default);
}
