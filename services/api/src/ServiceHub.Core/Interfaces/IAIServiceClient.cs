using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Interface for AI service operations including anomaly detection and message analysis.
/// </summary>
public interface IAIServiceClient
{
    /// <summary>
    /// Analyzes messages for anomalies.
    /// </summary>
    /// <param name="messages">The messages to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing detected anomaly types.</returns>
    Task<Result<IReadOnlyList<AnomalyType>>> AnalyzeMessagesAsync(
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets AI-generated insights for a message.
    /// </summary>
    /// <param name="message">The message to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the AI insights as text.</returns>
    Task<Result<string>> GetMessageInsightsAsync(
        Message message,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if the AI service is available.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating whether the service is available.</returns>
    Task<Result<bool>> IsAvailableAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Detects anomalies in a namespace within a time window.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="startTime">The start of the analysis window.</param>
    /// <param name="endTime">The end of the analysis window.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing detected anomalies.</returns>
    Task<Result<IReadOnlyList<Anomaly>>> DetectAnomaliesAsync(
        Guid namespaceId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an anomaly by its ID.
    /// </summary>
    /// <param name="anomalyId">The anomaly ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the anomaly.</returns>
    Task<Result<Anomaly>> GetAnomalyByIdAsync(
        Guid anomalyId,
        CancellationToken cancellationToken = default);
}
