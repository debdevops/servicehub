using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Intentionally-inert stub for the AI service client contract.
/// <para>
/// ServiceHub makes <b>no external AI API calls of any kind</b>. Its DLQ/message pattern
/// detection is heuristic and runs locally (client-side in the SPA and in local backend
/// heuristics). This stub exists only to satisfy the <see cref="IAIServiceClient"/> seam;
/// every method reports "not available" so callers degrade gracefully. It deliberately does
/// not — and must not — reach out to any hosted model or remote endpoint.
/// </para>
/// </summary>
public sealed class AIServiceClient : IAIServiceClient
{
    private readonly ILogger<AIServiceClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AIServiceClient"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public AIServiceClient(ILogger<AIServiceClient> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<AnomalyType>>> AnalyzeMessagesAsync(
        IReadOnlyList<Message> messages,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("AI service is not yet implemented. AnalyzeMessagesAsync called with {MessageCount} messages", messages?.Count ?? 0);

        return Task.FromResult(Result.Failure<IReadOnlyList<AnomalyType>>(Error.Internal(
            ErrorCodes.General.ServiceUnavailable,
            "AI anomaly detection service is not yet implemented. This feature will be available in a future release.")));
    }

    /// <inheritdoc/>
    public Task<Result<string>> GetMessageInsightsAsync(
        Message message,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("AI service is not yet implemented. GetMessageInsightsAsync called for message {MessageId}", message?.MessageId);

        return Task.FromResult(Result.Failure<string>(Error.Internal(
            ErrorCodes.General.ServiceUnavailable,
            "AI message insights service is not yet implemented. This feature will be available in a future release.")));
    }

    /// <inheritdoc/>
    public Task<Result<bool>> IsAvailableAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("AI service availability check - service is not yet implemented");

        // Return success with false to indicate the service is not available
        // This allows callers to gracefully handle the unavailability
        return Task.FromResult(Result.Success(false));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Anomaly>>> DetectAnomaliesAsync(
        Guid namespaceId,
        DateTimeOffset startTime,
        DateTimeOffset endTime,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "AI service is not yet implemented. DetectAnomaliesAsync called for namespace {NamespaceId} from {StartTime} to {EndTime}",
            namespaceId,
            startTime,
            endTime);

        return Task.FromResult(Result.Failure<IReadOnlyList<Anomaly>>(Error.Internal(
            ErrorCodes.General.ServiceUnavailable,
            "AI anomaly detection service is not yet implemented. This feature will be available in a future release.")));
    }

    /// <inheritdoc/>
    public Task<Result<Anomaly>> GetAnomalyByIdAsync(
        Guid anomalyId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("AI service is not yet implemented. GetAnomalyByIdAsync called for anomaly {AnomalyId}", anomalyId);

        return Task.FromResult(Result.Failure<Anomaly>(Error.Internal(
            ErrorCodes.General.ServiceUnavailable,
            "AI anomaly detection service is not yet implemented. This feature will be available in a future release.")));
    }
}
