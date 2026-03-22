using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Eleven exact-match rules that can classify a DLQ message with high confidence.
/// Returns <c>null</c> when no rule fires, letting the pipeline fall through to heuristics.
/// </summary>
internal static class DeterministicClassifier
{
    internal sealed record Hit(FailureCategory Category, double Confidence, string RootCause);

    /// <summary>
    /// Evaluates all deterministic rules against the given message.
    /// </summary>
    internal static Hit? Evaluate(DlqMessage msg)
    {
        var reason = msg.DeadLetterReason ?? string.Empty;
        var desc = msg.DeadLetterErrorDescription ?? string.Empty;
        var combined = SignalExtractor.CombinedText(msg);

        // Rule 1 – MaxDeliveryCountExceeded (Service Bus system reason)
        if (reason.Contains("MaxDeliveryCount", StringComparison.OrdinalIgnoreCase))
            return new Hit(FailureCategory.MaxDelivery, 0.99,
                $"Service Bus exceeded max delivery count ({msg.DeliveryCount} attempts).");

        // Rule 2 – TTLExpiredException
        if (reason.Contains("TTLExpiredException", StringComparison.OrdinalIgnoreCase))
            return new Hit(FailureCategory.Expired, 0.99,
                "Message time-to-live expired before delivery.");

        // Rule 3 – HeaderSizeExceeded
        if (reason.Contains("HeaderSizeExceeded", StringComparison.OrdinalIgnoreCase))
            return new Hit(FailureCategory.QuotaExceeded, 0.98,
                "Message header size exceeded the Service Bus limit.");

        // Rule 4 – MessageSizeExceeded
        if (reason.Contains("MessageSizeExceeded", StringComparison.OrdinalIgnoreCase))
            return new Hit(FailureCategory.QuotaExceeded, 0.98,
                "Message body size exceeded the Service Bus limit.");

        // Rule 5 – SessionFilterMismatch
        if (reason.Contains("SessionFilter", StringComparison.OrdinalIgnoreCase))
            return new Hit(FailureCategory.ProcessingError, 0.95,
                "Message session filter did not match any active session.");

        // Rule 6 – HTTP 401 Unauthorized
        if (combined.Contains("401") && combined.Contains("unauthorized"))
            return new Hit(FailureCategory.Authorization, 0.97,
                "Consumer received HTTP 401 Unauthorized.");

        // Rule 7 – HTTP 403 Forbidden
        if (combined.Contains("403") && combined.Contains("forbidden"))
            return new Hit(FailureCategory.Authorization, 0.97,
                "Consumer received HTTP 403 Forbidden.");

        // Rule 8 – JSON deserialization failure
        if (combined.Contains("jsonexception") || combined.Contains("json deserialization"))
            return new Hit(FailureCategory.DataQuality, 0.95,
                "JSON deserialization failed — likely schema mismatch.");

        // Rule 9 – Connection refused
        if (combined.Contains("connection refused"))
            return new Hit(FailureCategory.Transient, 0.93,
                "Downstream service refused the connection (likely down).");

        // Rule 10 – SQL timeout
        if (combined.Contains("sqltimeout") || (combined.Contains("sql") && combined.Contains("timeout")))
            return new Hit(FailureCategory.Transient, 0.92,
                "SQL query timed out during message processing.");

        // Rule 11 – Explicit dead-letter by application ("reason = <app-supplied>")
        if (!string.IsNullOrWhiteSpace(reason)
            && !reason.Contains("MaxDeliveryCount", StringComparison.OrdinalIgnoreCase)
            && !reason.Contains("TTL", StringComparison.OrdinalIgnoreCase)
            && desc.Contains("exception", StringComparison.OrdinalIgnoreCase))
            return new Hit(FailureCategory.ProcessingError, 0.90,
                $"Application explicitly dead-lettered: {reason}");

        return null;
    }
}
