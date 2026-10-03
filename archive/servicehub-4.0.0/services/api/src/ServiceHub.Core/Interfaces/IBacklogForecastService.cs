using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Deterministic, arithmetic projection of when an entity's DLQ backlog will cross an alert
/// threshold, based on its recent growth rate. No ML, no LLM: a linear extrapolation of counts
/// already in <c>DlqMessages</c> (roadmap §5.E, P4 — "Predictive backlog signal").
/// </summary>
public interface IBacklogForecastService
{
    /// <summary>
    /// Forecasts, for every entity in <paramref name="namespaceId"/> with a growing backlog,
    /// when its active DLQ message count will cross <paramref name="alertThreshold"/>.
    /// </summary>
    /// <param name="namespaceId">The namespace to analyze.</param>
    /// <param name="startTime">The start of the trailing window used to estimate growth rate.</param>
    /// <param name="endTime">The end of the trailing window (exclusive), and forecast reference point.</param>
    /// <param name="alertThreshold">
    /// The backlog depth considered a breach. Null uses the service's configured default.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A result containing every entity whose backlog is growing and projected to cross the
    /// threshold within the service's forecast horizon. An empty list is a valid, successful
    /// result — it means nothing is currently on a breach trajectory.
    /// </returns>
    Task<Result<IReadOnlyList<BacklogForecast>>> ForecastAsync(
        Guid namespaceId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        int? alertThreshold = null,
        CancellationToken cancellationToken = default);
}
