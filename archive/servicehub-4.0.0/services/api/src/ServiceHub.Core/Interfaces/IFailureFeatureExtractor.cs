using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Extracts observable failure characteristics from DLQ messages.
///
/// This is a reusable, strategy-independent service. Every clustering strategy
/// receives pre-extracted features rather than extracting them internally, eliminating
/// duplication and ensuring consistency across strategies.
///
/// Feature extraction is deterministic and has no side effects.
/// </summary>
public interface IFailureFeatureExtractor
{
    /// <summary>
    /// Extract failure characteristics from a single DLQ message.
    /// </summary>
    /// <param name="message">The message to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Immutable FailureFeatures object.</returns>
    Task<Result<FailureFeatures>> ExtractAsync(
        DlqMessage message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch extraction for better performance.
    /// </summary>
    /// <param name="messages">Messages to extract features from.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of FailureFeatures, one per message in order.</returns>
    Task<Result<IReadOnlyList<FailureFeatures>>> ExtractBatchAsync(
        IReadOnlyList<DlqMessage> messages,
        CancellationToken cancellationToken = default);
}
