using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.Gcp;

/// <summary>
/// GCP Pub/Sub-specific deterministic rules that extend the base forensic engine.
/// Rules are evaluated before the Azure-oriented rules in <c>DeterministicClassifier</c>.
/// </summary>
public static class GcpForensicExtensions
{
    /// <summary>
    /// Result of a single forensic rule evaluation.
    /// </summary>
    /// <param name="Category">The failure category determined by this rule.</param>
    /// <param name="Confidence">Confidence score in the range [0, 1].</param>
    /// <param name="RootCause">Human-readable root cause explanation.</param>
    public sealed record ForensicHit(FailureCategory Category, double Confidence, string RootCause);

    /// <summary>
    /// Evaluates all GCP Pub/Sub-specific rules against the given DLQ message.
    /// Returns the first matching rule's result, or <see langword="null"/> if no rule fires.
    /// </summary>
    /// <param name="msg">The dead-letter message to evaluate.</param>
    /// <returns>A <see cref="ForensicHit"/> if a rule matched; otherwise <see langword="null"/>.</returns>
    public static ForensicHit? Evaluate(DlqMessage msg)
    {
        ArgumentNullException.ThrowIfNull(msg);

        var reason = msg.DeadLetterReason ?? string.Empty;
        var desc = msg.DeadLetterErrorDescription ?? string.Empty;
        var propsJson = msg.ApplicationPropertiesJson ?? string.Empty;

        // Rule 1 — GcpMaxDeliveryAttempts
        // Pub/Sub forwards messages to the dead-letter topic when MaxDeliveryAttempts is reached.
        // The forwarded message includes a CloudPubSubDeadLetterSourceSubscription attribute.
        if (reason.Equals("maxDeliveryAttempts", StringComparison.OrdinalIgnoreCase) ||
            propsJson.Contains("CloudPubSubDeadLetterSourceSubscription", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("max delivery attempts", StringComparison.OrdinalIgnoreCase))
        {
            return new ForensicHit(
                FailureCategory.MaxDelivery,
                0.99,
                "Pub/Sub message exceeded MaxDeliveryAttempts on the subscription. " +
                "The message was delivered and not acknowledged more times than the configured limit. " +
                "Inspect the consumer logs to identify the processing error.");
        }

        // Rule 2 — GcpAckDeadlineExpiry
        // When a message has been attempted multiple times and the error description references
        // ack deadline, the consumer is likely taking too long to process.
        if (msg.DeliveryCount > 1 &&
            (desc.Contains("ack_deadline", StringComparison.OrdinalIgnoreCase) ||
             desc.Contains("AckDeadline", StringComparison.OrdinalIgnoreCase) ||
             propsJson.Contains("\"ackDeadlineSeconds\"", StringComparison.OrdinalIgnoreCase)))
        {
            return new ForensicHit(
                FailureCategory.Transient,
                0.87,
                "Pub/Sub message re-delivered after ack deadline expired — subscriber likely took too long " +
                "to process. Consider increasing AckDeadlineSeconds (up to 600) or optimising processing time. " +
                "Also consider using StreamingPull with ModifyAckDeadline for long-running operations.");
        }

        // Rule 3 — GcpNack
        // When a subscriber explicitly nacks a message (signals failure), Pub/Sub re-delivers.
        // After MaxDeliveryAttempts it lands in the dead-letter topic.
        if (reason.Equals("nack", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("NACK", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("subscriber nacked", StringComparison.OrdinalIgnoreCase))
        {
            return new ForensicHit(
                FailureCategory.ProcessingError,
                0.91,
                "Subscriber explicitly nacked this message, signalling a processing error. " +
                "Check the subscriber application logs for the specific error that caused the nack.");
        }

        // Rule 4 — GcpOrderingKeyStall
        // When message ordering is enabled and a message fails, all subsequent messages with the
        // same ordering key are blocked until the failed message is resolved.
        if (propsJson.Contains("\"orderingKey\"", StringComparison.OrdinalIgnoreCase) ||
            propsJson.Contains("\"OrderingKey\"", StringComparison.OrdinalIgnoreCase) ||
            reason.Contains("ordering", StringComparison.OrdinalIgnoreCase))
        {
            return new ForensicHit(
                FailureCategory.ProcessingError,
                0.82,
                "Message with an ordering key is in the dead-letter queue. " +
                "When message ordering is enabled, a stalled ordering key blocks all subsequent messages " +
                "with the same key. Resume delivery by calling ModifyAckDeadline(0) on the stalled message " +
                "after fixing the underlying processing error.");
        }

        return null;
    }
}
