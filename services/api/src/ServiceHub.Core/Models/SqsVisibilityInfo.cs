namespace ServiceHub.Core.Models;

/// <summary>
/// Snapshot of SQS in-flight and dead-letter message counts for a single queue.
/// Surfaces the most common AWS operational pain point: messages that are "invisible"
/// because they are within their visibility timeout window.
/// </summary>
/// <param name="InFlightCount">
/// Number of messages currently in-flight (received but not yet deleted or visibility-timeout-expired).
/// These messages are invisible to other consumers and will NOT appear in a standard peek.
/// A non-zero value often indicates consumers are processing or crashing mid-processing.
/// </param>
/// <param name="VisibilityTimeoutSeconds">
/// The queue's configured visibility timeout in seconds.
/// Messages that are not deleted within this window reappear in the queue.
/// </param>
/// <param name="DlqCount">
/// Number of messages in the queue's associated Dead-Letter Queue (if a RedrivePolicy is set).
/// Zero if no DLQ is configured.
/// </param>
public sealed record SqsVisibilityInfo(
    int InFlightCount,
    int VisibilityTimeoutSeconds,
    int DlqCount);
