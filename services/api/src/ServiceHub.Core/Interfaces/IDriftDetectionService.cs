using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Deterministic message-shape baseline and drift detection over already-stored
/// <see cref="MessageFeatureRecord"/> data. No ML, no LLM, no new data source: every finding is
/// a reproducible function of the <c>SchemaFingerprint</c>/<c>PayloadShape</c> fields already
/// computed and persisted for every dead-lettered message (roadmap §5.C, P1 "Baseline the good" /
/// P2 "Drift detection").
/// </summary>
public interface IDriftDetectionService
{
    /// <summary>
    /// Detects per-entity message-shape drift in <paramref name="namespaceId"/> by comparing the
    /// dominant schema fingerprint and payload format in <c>[startTime, endTime)</c> against the
    /// baseline established over the same-length trailing periods immediately preceding it.
    /// </summary>
    /// <param name="namespaceId">The namespace to analyze.</param>
    /// <param name="startTime">The start of the current analysis window.</param>
    /// <param name="endTime">The end of the current analysis window (exclusive).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A result containing every entity whose current-window message shape deviates enough from
    /// its baseline to be flagged. An empty list is a valid, successful result.
    /// </returns>
    Task<Result<IReadOnlyList<DriftFinding>>> DetectDriftAsync(
        Guid namespaceId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default);
}
