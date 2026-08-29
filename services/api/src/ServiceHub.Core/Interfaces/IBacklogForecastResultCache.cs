using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Short-lived, in-process store of recently computed <see cref="BacklogForecast"/> instances.
/// </summary>
/// <remarks>
/// Deliberately not backed by the database, for the same reason as <see cref="IAnomalyResultCache"/>:
/// <c>BacklogForecast</c> has never had an EF Core migration, and the RC1 migration freeze
/// (ADR-0006) forbids adding one — regardless of how additive — while it is in effect. This cache
/// lets <c>GET /v1/backlog-forecasts/{id}</c> retrieve a result a recent forecast cycle already
/// computed without introducing schema. Entries do not survive a process restart and are not
/// shared across instances; this is an accepted, documented limitation, not an oversight.
/// </remarks>
public interface IBacklogForecastResultCache
{
    /// <summary>
    /// Stores (or refreshes) a batch of freshly computed forecasts, keyed by their <see cref="BacklogForecast.Id"/>.
    /// </summary>
    void Store(IEnumerable<BacklogForecast> forecasts);

    /// <summary>
    /// Attempts to retrieve a previously stored forecast by ID.
    /// </summary>
    /// <returns>The forecast, or <c>null</c> if it was never stored or has expired.</returns>
    BacklogForecast? TryGet(Guid id);
}
