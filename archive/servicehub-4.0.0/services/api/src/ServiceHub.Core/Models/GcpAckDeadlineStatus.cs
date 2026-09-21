namespace ServiceHub.Core.Models;

/// <summary>
/// Captures the ack-deadline configuration for a GCP Pub/Sub subscription,
/// along with the dead-letter policy and message ordering settings.
/// <para>
/// This is the GCP equivalent of <c>SqsVisibilityInfo</c> — both surface
/// the most common "invisible message" debugging scenario on their respective platforms.
/// </para>
/// </summary>
/// <param name="AckDeadlineSeconds">
/// Number of seconds the subscriber has to acknowledge a message before it is re-delivered.
/// Default is 10 seconds; can be set from 10 to 600 seconds.
/// </param>
/// <param name="HasDeadLetterPolicy">Whether a dead-letter topic is configured.</param>
/// <param name="DeadLetterTopic">The full resource name of the dead-letter topic, if configured.</param>
/// <param name="MaxDeliveryAttempts">
/// Maximum delivery attempts before a message is forwarded to the dead-letter topic.
/// Null when no dead-letter policy is configured.
/// </param>
/// <param name="MessageOrderingEnabled">
/// Whether message ordering is enabled for this subscription.
/// When true, messages with the same ordering key are delivered in order.
/// </param>
public sealed record GcpAckDeadlineStatus(
    int AckDeadlineSeconds,
    bool HasDeadLetterPolicy,
    string? DeadLetterTopic,
    int? MaxDeliveryAttempts,
    bool MessageOrderingEnabled);
