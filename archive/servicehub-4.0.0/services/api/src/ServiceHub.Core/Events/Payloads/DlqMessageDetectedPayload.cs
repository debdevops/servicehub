namespace ServiceHub.Core.Events.Payloads;

/// <summary>
/// Payload for <see cref="EventTypes.DlqMessageDetected"/>.
/// Carries identifying information about a single DLQ message that the monitor
/// has seen for the first time in the current scan cycle.
/// </summary>
public sealed record DlqMessageDetectedPayload
{
    /// <summary>Internal DLQ record primary key assigned by the SQLite store.</summary>
    public required long DlqRecordId { get; init; }

    /// <summary>Service Bus (or provider-equivalent) message identifier.</summary>
    public required string MessageId { get; init; }

    /// <summary>Sequence number of the message within the dead-letter queue.</summary>
    public required long SequenceNumber { get; init; }

    /// <summary>Identifier of the namespace where the message was detected.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Name of the queue, topic, or subscription that hosts the DLQ.</summary>
    public required string EntityName { get; init; }

    /// <summary>Heuristic failure category assigned at detection time.</summary>
    public required string FailureCategory { get; init; }

    /// <summary>Dead-letter reason string provided by the messaging provider.</summary>
    public string? DeadLetterReason { get; init; }

    /// <summary>UTC timestamp when the message was first detected by the monitor.</summary>
    public required DateTimeOffset DetectedAtUtc { get; init; }

    /// <summary>Correlation ID propagated from the original message, if present.</summary>
    public string? CorrelationId { get; init; }
}
