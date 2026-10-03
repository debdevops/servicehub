namespace ServiceHub.Shared.Helpers;

/// <summary>
/// Extracts a correlation/trace ID from a message's application properties, for providers
/// (AWS SQS, GCP Pub/Sub) whose SDK has no dedicated CorrelationId field of its own — unlike
/// Azure Service Bus, where the SDK's native CorrelationId property is used directly.
/// </summary>
public static class MessageCorrelationIdExtractor
{
    /// <summary>
    /// Common correlation-ID key names, in priority order, used across AWS SQS message
    /// attributes and GCP Pub/Sub attributes. Mirrors the key names ServiceHub itself accepts
    /// on send and Cross-Cloud Trace checks when searching live messages.
    /// </summary>
    private static readonly string[] CorrelationKeys =
        ["CorrelationId", "correlationId", "correlation_id", "TraceId", "traceId", "trace_id"];

    /// <summary>
    /// Extracts a correlation ID from an application-properties dictionary, or null if none of
    /// the recognised key names is present.
    /// </summary>
    public static string? Extract(IReadOnlyDictionary<string, object>? properties)
    {
        if (properties is null) return null;

        foreach (var key in CorrelationKeys)
        {
            if (properties.TryGetValue(key, out var value) && value is string s && !string.IsNullOrWhiteSpace(s))
                return s;
        }

        return null;
    }
}
