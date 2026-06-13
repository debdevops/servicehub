using ServiceHub.Core.Enums;

namespace ServiceHub.Simulator.Store;

/// <summary>
/// Immutable snapshot of a simulated message in the in-memory store.
/// All fields are copied from real SDK message types so simulated receivers
/// can produce <see cref="ServiceHub.Core.Entities.Message"/> values without SDK references.
/// </summary>
public sealed record SimulatorMessage(
    string MessageId,
    long SequenceNumber,
    string Body,
    string? ContentType,
    string? CorrelationId,
    string? SessionId,
    string? PartitionKey,
    string? Subject,
    int DeliveryCount,
    DateTimeOffset EnqueuedTime,
    DateTimeOffset? ScheduledEnqueueTime,
    bool IsDeadLettered,
    string? DeadLetterReason,
    string? DeadLetterErrorDescription,
    IReadOnlyDictionary<string, object> ApplicationProperties,
    long SizeInBytes,
    // AWS-specific
    string? ReceiptHandle,
    DateTimeOffset? VisibilityUntil,
    // GCP-specific
    string? OrderingKey,
    int DeliveryAttempt,
    DateTimeOffset? AckDeadline,
    bool IsNacked,
    CloudProviderType Provider
);
