using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Extracts observable failure characteristics from DLQ messages.
/// This implementation is strategy-independent and reusable.
/// </summary>
public sealed class FailureFeatureExtractor : IFailureFeatureExtractor
{
    public async Task<Result<FailureFeatures>> ExtractAsync(
        DlqMessage message,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);

        var features = ExtractFromMessage(message);
        return await Task.FromResult(Result.Success(features)).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<FailureFeatures>>> ExtractBatchAsync(
        IReadOnlyList<DlqMessage> messages,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var featuresList = messages.Select(ExtractFromMessage).ToList();
        return await Task.FromResult(Result.Success((IReadOnlyList<FailureFeatures>)featuresList))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Extract failure characteristics from a single message.
    /// This logic is deterministic and identical across all strategies.
    /// </summary>
    private static FailureFeatures ExtractFromMessage(DlqMessage message)
    {
        var detectedAt = message.DeadLetterTimeUtc ?? message.DetectedAtUtc;
        var timeToDeadletter = (detectedAt - message.EnqueuedTimeUtc).TotalSeconds;
        var secondsSinceEnqueued = (DateTimeOffset.UtcNow - message.EnqueuedTimeUtc).TotalSeconds;

        return new FailureFeatures
        {
            DeadLetterReason = message.DeadLetterReason ?? "Unknown",
            EntityName = message.EntityName ?? "Unknown",
            Provider = message.CloudProvider,
            FailureCategory = message.FailureCategory.ToString(),
            DeliveryCount = message.DeliveryCount,
            ExceptionType = ExtractExceptionType(message),
            ErrorTextNormalized = NormalizeErrorText(message),
            MessageSize = message.MessageSize,
            ContentType = message.ContentType,
            PropertyCount = CountProperties(message),
            HourOfDay = detectedAt.Hour,
            DayOfWeek = (int)detectedAt.DayOfWeek,
            TimeToDeadletterSeconds = timeToDeadletter,
            SecondsSinceEnqueuedSeconds = secondsSinceEnqueued,
        };
    }

    /// <summary>
    /// Extract exception type from message properties or error description.
    /// </summary>
    private static string? ExtractExceptionType(DlqMessage message)
    {
        var errorText = (message.DeadLetterErrorDescription ?? message.BodyPreview ?? string.Empty)
            .ToLowerInvariant();

        // Check application properties first
        if (!string.IsNullOrWhiteSpace(message.ApplicationPropertiesJson) &&
            message.ApplicationPropertiesJson.Contains("ExceptionType", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var start = message.ApplicationPropertiesJson.IndexOf("ExceptionType", StringComparison.OrdinalIgnoreCase);
                if (start >= 0)
                {
                    var end = message.ApplicationPropertiesJson.IndexOf("\"", start + 15);
                    if (end > start)
                    {
                        return message.ApplicationPropertiesJson.Substring(start + 15, end - start - 15);
                    }
                }
            }
            catch
            {
                // Fall through to heuristic patterns
            }
        }

        // Heuristic patterns
        if (errorText.Contains("timeout")) return "TimeoutException";
        if (errorText.Contains("authentication")) return "AuthenticationException";
        if (errorText.Contains("authorization")) return "AuthorizationException";
        if (errorText.Contains("notfound")) return "NotFoundException";
        if (errorText.Contains("serialization")) return "SerializationException";
        if (errorText.Contains("validation")) return "ValidationException";

        return null;
    }

    /// <summary>
    /// Normalize error text for consistent feature extraction.
    /// </summary>
    private static string? NormalizeErrorText(DlqMessage message)
    {
        var errorText = message.DeadLetterErrorDescription ?? message.BodyPreview;
        if (string.IsNullOrWhiteSpace(errorText))
        {
            return null;
        }

        return errorText
            .ToLowerInvariant()
            .Trim()
            .Replace("\n", " ")
            .Replace("\r", " ");
    }

    /// <summary>
    /// Count user-defined properties on the message.
    /// </summary>
    private static int CountProperties(DlqMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.ApplicationPropertiesJson))
        {
            return 0;
        }

        try
        {
            // Simple heuristic: count colons (property separators in JSON)
            return message.ApplicationPropertiesJson.Count(c => c == ':');
        }
        catch
        {
            return 0;
        }
    }
}
