using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Short-lived, in-process store of recently generated <see cref="Narration"/> instances.
/// </summary>
/// <remarks>
/// Deliberately not backed by the database, for the same reason as
/// <see cref="IAnomalyResultCache"/>/<see cref="IDriftResultCache"/>/<see cref="ICorrelationResultCache"/>:
/// <c>Narration</c> has never had an EF Core migration, and the RC1 migration freeze (ADR-0006)
/// forbids adding one while it is in effect. This cache lets <c>GET /v1/narrations/{id}</c>
/// retrieve a result a recent cycle already computed without introducing schema. Entries do not
/// survive a process restart and are not shared across instances; this is an accepted, documented
/// limitation, not an oversight.
/// </remarks>
public interface INarrationResultCache
{
    /// <summary>
    /// Stores (or refreshes) a batch of freshly generated narrations, keyed by their
    /// <see cref="Narration.Id"/>.
    /// </summary>
    void Store(IEnumerable<Narration> narrations);

    /// <summary>
    /// Attempts to retrieve a previously stored narration by ID.
    /// </summary>
    /// <returns>The narration, or <c>null</c> if it was never stored or has expired.</returns>
    Narration? TryGet(Guid id);
}
