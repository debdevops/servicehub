using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Short-lived, in-process store of recently detected <see cref="ExternalSignalCorrelation"/>
/// instances — the same non-persisted, TTL-bounded pattern <see cref="ICorrelationResultCache"/>
/// uses for C1/C2, applied to C3. The raw signal (<see cref="ExternalSignalEvent"/>) is durable
/// (M5); the correlation hypothesis derived from it is reproducible from that signal plus the
/// anomaly detector's own output, so it does not need its own table either.
/// </summary>
public interface IExternalSignalCorrelationCache
{
    /// <summary>
    /// Stores (or refreshes) a batch of freshly detected correlations, keyed by their
    /// <see cref="ExternalSignalCorrelation.Id"/>.
    /// </summary>
    void Store(IEnumerable<ExternalSignalCorrelation> correlations);

    /// <summary>
    /// Attempts to retrieve a previously stored correlation by ID.
    /// </summary>
    /// <returns>The correlation, or <c>null</c> if it was never stored or has expired.</returns>
    ExternalSignalCorrelation? TryGet(Guid id);
}
