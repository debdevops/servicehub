using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Builds the wire payload for a webhook notification in one specific
/// <see cref="WebhookFormat"/>. <see cref="IWebhookNotifier"/> selects the formatter matching
/// <c>WebhookOptions.Format</c> and POSTs whatever object it returns — new destinations
/// (a future chat platform, for example) are a new implementation of this interface, not a
/// change to <see cref="IWebhookNotifier"/> or any existing formatter (Open/Closed).
/// </summary>
public interface IWebhookMessageFormatter
{
    /// <summary>The format this instance builds payloads for.</summary>
    WebhookFormat Format { get; }

    /// <summary>Builds the payload object for a DLQ spike alert. Serialized as JSON by the caller.</summary>
    object BuildDlqSpikePayload(DlqSpikeNotification notification);

    /// <summary>Builds the payload object for a bulk operation completion alert. Serialized as JSON by the caller.</summary>
    object BuildBulkOperationCompletedPayload(BulkOperationCompletedNotification notification);

    /// <summary>Builds the payload object for an autonomy grant transition alert. Serialized as JSON by the caller.</summary>
    object BuildAutonomyTransitionPayload(AutonomyTransitionNotification notification);

    /// <summary>Builds the payload object for a circuit breaker trip alert. Serialized as JSON by the caller.</summary>
    object BuildCircuitBreakerTrippedPayload(CircuitBreakerTrippedNotification notification);
}
