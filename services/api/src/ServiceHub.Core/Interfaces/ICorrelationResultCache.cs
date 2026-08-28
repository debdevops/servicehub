using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Short-lived, in-process store of recently detected <see cref="CorrelationFinding"/> instances.
/// </summary>
/// <remarks>
/// Deliberately not backed by the database: <c>CorrelationFinding</c> has never had an EF Core
/// migration, and the RC1 migration freeze (ADR-0006) forbids adding one — regardless of how
/// additive — while it is in effect. This cache lets <c>GET /v1/correlation-findings/{id}</c>
/// retrieve a result a recent detection cycle already computed without introducing schema.
/// Entries do not survive a process restart and are not shared across instances; this is an
/// accepted, documented limitation, not an oversight.
/// </remarks>
public interface ICorrelationResultCache
{
    /// <summary>
    /// Stores (or refreshes) a batch of freshly detected correlation findings, keyed by their
    /// <see cref="CorrelationFinding.Id"/>.
    /// </summary>
    void Store(IEnumerable<CorrelationFinding> findings);

    /// <summary>
    /// Attempts to retrieve a previously stored correlation finding by ID.
    /// </summary>
    /// <returns>The finding, or <c>null</c> if it was never stored or has expired.</returns>
    CorrelationFinding? TryGet(Guid id);
}
