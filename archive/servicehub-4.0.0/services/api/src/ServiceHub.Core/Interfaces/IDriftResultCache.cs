using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Durable store of detected <see cref="DriftFinding"/> instances (roadmap next-chapter M1,
/// ADR-0009).
/// </summary>
/// <remarks>
/// Backed by <c>DlqDbContext.DriftFindings</c> as of M1 — before this, findings lived in a
/// process-local, 24-hour TTL cache, so a <see cref="Entities.PlaybookEntry.EvidenceRefJson"/>
/// citation written by <c>PreventionRuleEvaluationService</c> (permanent, hash-chained) stopped
/// resolving within a day of the finding it cites. See <c>SqliteDriftResultCache</c> and
/// <c>PillarFindingRetentionWorker</c>, whose retention sweep never deletes a finding a
/// <see cref="Entities.PlaybookEntry"/> still cites.
/// </remarks>
public interface IDriftResultCache
{
    /// <summary>
    /// Stores (or refreshes) a batch of freshly detected drift findings, keyed by their
    /// <see cref="DriftFinding.Id"/>.
    /// </summary>
    /// <param name="ownerId">The owner the findings were detected for.</param>
    /// <param name="findings">The findings to store.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task StoreAsync(string ownerId, IEnumerable<DriftFinding> findings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to retrieve a previously stored drift finding by ID.
    /// </summary>
    /// <returns>The finding, or <c>null</c> if it was never stored or has since been pruned by
    /// retention.</returns>
    Task<DriftFinding?> TryGetAsync(Guid id, CancellationToken cancellationToken = default);
}
