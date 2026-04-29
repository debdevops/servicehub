using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.Aws;

/// <summary>
/// AWS-specific deterministic rules that extend the base forensic engine.
/// Rules are evaluated before the Azure-oriented rules in <c>DeterministicClassifier</c>.
/// Each rule returns a <see cref="ForensicHit"/> when it fires, or <see langword="null"/> to fall through.
/// </summary>
public static class AwsForensicExtensions
{
    /// <summary>
    /// Result of a single forensic rule evaluation.
    /// </summary>
    /// <param name="Category">The failure category determined by this rule.</param>
    /// <param name="Confidence">Confidence score in the range [0, 1].</param>
    /// <param name="RootCause">Human-readable root cause explanation.</param>
    public sealed record ForensicHit(FailureCategory Category, double Confidence, string RootCause);

    /// <summary>
    /// Evaluates all AWS-specific rules against the given DLQ message.
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

        // Rule 1 — AwsSqsMaxReceive
        // SQS sets DeadLetterReason to "MaxReceiveCount" when the redrive policy threshold is hit.
        if (reason.Equals("MaxReceiveCount", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("maxReceiveCount", StringComparison.OrdinalIgnoreCase))
        {
            return new ForensicHit(
                FailureCategory.MaxDelivery,
                0.99,
                "SQS message exceeded MaxReceiveCount redrive policy threshold. " +
                "The message was received and not deleted more times than the configured limit.");
        }

        // Rule 2 — AwsLambdaError
        // Lambda invocation errors include a RequestID and an ErrorCode in message attributes.
        if (propsJson.Contains("\"RequestID\"", StringComparison.OrdinalIgnoreCase) &&
            propsJson.Contains("\"ErrorCode\"", StringComparison.OrdinalIgnoreCase))
        {
            var errorCode = ExtractJsonValue(propsJson, "ErrorCode") ?? "unknown";
            return new ForensicHit(
                FailureCategory.ProcessingError,
                0.93,
                $"Lambda invocation error: {errorCode}. " +
                "Check Lambda execution logs for the full stack trace.");
        }

        // Rule 3 — AwsKmsError
        // KMS decryption failures occur when the queue is encrypted but the consumer
        // or message sender lacks kms:Decrypt permissions.
        if (desc.Contains("KMS", StringComparison.OrdinalIgnoreCase) ||
            desc.Contains("kms:Decrypt", StringComparison.OrdinalIgnoreCase))
        {
            return new ForensicHit(
                FailureCategory.Authorization,
                0.97,
                "KMS key not accessible — check IAM permissions for kms:Decrypt on the queue's " +
                "encryption key. The consumer's IAM role must have kms:Decrypt on the KMS key used " +
                "to encrypt this queue.");
        }

        // Rule 4 — AwsVisibilityExpiry
        // When a message has been delivered more than once and the attributes reference
        // VisibilityTimeout, the consumer likely crashed or exceeded its processing budget.
        if (msg.DeliveryCount > 1 &&
            propsJson.Contains("VisibilityTimeout", StringComparison.OrdinalIgnoreCase))
        {
            return new ForensicHit(
                FailureCategory.Transient,
                0.85,
                "Message re-queued after visibility timeout expired — consumer likely crashed or " +
                "took too long to process. Consider increasing VisibilityTimeout or optimising " +
                "consumer processing time.");
        }

        return null;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static string? ExtractJsonValue(string json, string key)
    {
        // Simple substring extraction — avoids a full JSON parse dependency.
        var searchKey = $"\"{key}\"";
        var idx = json.IndexOf(searchKey, StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return null;

        var colonIdx = json.IndexOf(':', idx + searchKey.Length);
        if (colonIdx < 0) return null;

        var valueStart = json.IndexOf('"', colonIdx + 1);
        if (valueStart < 0) return null;

        var valueEnd = json.IndexOf('"', valueStart + 1);
        if (valueEnd < 0) return null;

        return json[(valueStart + 1)..valueEnd];
    }
}
