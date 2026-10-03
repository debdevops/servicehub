using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// Immutable collection of observable failure characteristics extracted from a DLQ message.
/// These features are the foundation for fingerprinting and are strategy-independent.
/// Extraction is deterministic and reusable across all clustering strategies.
/// </summary>
public sealed record FailureFeatures
{
    /// <summary>Primary failure reason (e.g., MaxDeliveryCountExceeded, TTLExpiredException).</summary>
    public required string DeadLetterReason { get; init; }

    /// <summary>The queue, topic, or subscription where this message was dead-lettered.</summary>
    public required string EntityName { get; init; }

    /// <summary>Cloud provider type (Azure, AWS, GCP).</summary>
    public required CloudProviderType Provider { get; init; }

    /// <summary>Forensic classification of the failure (if available).</summary>
    public string? FailureCategory { get; init; }

    /// <summary>Number of delivery attempts.</summary>
    public int DeliveryCount { get; init; }

    /// <summary>Exception type extracted from error text (if available).</summary>
    public string? ExceptionType { get; init; }

    /// <summary>Normalized and sanitized error text for analysis.</summary>
    public string? ErrorTextNormalized { get; init; }

    /// <summary>Size of the message body in bytes.</summary>
    public long MessageSize { get; init; }

    /// <summary>MIME type of the message content.</summary>
    public string? ContentType { get; init; }

    /// <summary>Number of user properties on the message.</summary>
    public int PropertyCount { get; init; }

    /// <summary>Hour of day when the message was dead-lettered (0-23, UTC).</summary>
    public int HourOfDay { get; init; }

    /// <summary>Day of week when the message was dead-lettered (0-6, Sunday=0, UTC).</summary>
    public int DayOfWeek { get; init; }

    /// <summary>Time elapsed from enqueue to dead-letter in seconds.</summary>
    public double TimeToDeadletterSeconds { get; init; }

    /// <summary>Seconds since the message was enqueued.</summary>
    public double SecondsSinceEnqueuedSeconds { get; init; }
}
