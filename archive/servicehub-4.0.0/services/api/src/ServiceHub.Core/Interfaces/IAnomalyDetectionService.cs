using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Deterministic, statistics-based anomaly detection over stored DLQ message history.
/// No ML, no LLM: every anomaly is a reproducible function of counts already in
/// <c>DlqMessages</c> (roadmap §5.B, I3 — "Anomalize").
/// </summary>
public interface IAnomalyDetectionService
{
    /// <summary>
    /// Detects per-entity volume anomalies in <paramref name="namespaceId"/> by comparing the
    /// message count in <c>[startTime, endTime)</c> against the mean/standard-deviation of the
    /// same-length trailing baseline periods immediately preceding it.
    /// </summary>
    /// <param name="namespaceId">The namespace to analyze.</param>
    /// <param name="startTime">The start of the current analysis window.</param>
    /// <param name="endTime">The end of the current analysis window (exclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A result containing every entity whose current-window count deviates enough from its
    /// baseline to be flagged. An empty list is a valid, successful result.
    /// </returns>
    Task<Result<IReadOnlyList<Anomaly>>> DetectAnomaliesAsync(
        Guid namespaceId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default);
}
