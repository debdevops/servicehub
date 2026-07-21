using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// Declares which operations a <see cref="Interfaces.ICloudMessagingProvider"/> genuinely
/// supports, so callers (API responses, UI) can ask "can this provider do X?" once, in one
/// place, instead of re-deriving the answer from scattered provider-type checks or discovering
/// it only when an operation degrades/fails at call time.
/// </summary>
/// <remarks>
/// These are facts about the messaging platform itself, not about a specific namespace or
/// connection — every namespace on a given provider shares the same capabilities. A new
/// provider (Kafka, RabbitMQ, …) declares its own <see cref="ProviderCapabilities"/> once,
/// via a static preset analogous to <see cref="Azure"/>/<see cref="Aws"/>/<see cref="Gcp"/>,
/// rather than requiring every call site that currently branches on
/// <see cref="CloudProviderType"/> to grow a new case.
/// </remarks>
/// <param name="SupportsMessageCounts">
/// Whether the provider can report a real active-message count. GCP Pub/Sub has no direct
/// count API and normalises to 0, which is otherwise indistinguishable from "actually empty."
/// </param>
/// <param name="SupportsManualDeadLetter">
/// Whether an operator can move a specific message to the dead-letter queue on demand.
/// GCP Pub/Sub dead-lettering is policy-driven (<c>MaxDeliveryAttempts</c>) only.
/// </param>
/// <param name="SupportsPurge">
/// Whether a single message can be permanently deleted by identity. Azure Service Bus has no
/// reliable single-message delete by sequence number.
/// </param>
/// <param name="SupportsScheduledMessages">
/// Whether the provider exposes a queryable, cancellable scheduled/delayed-delivery feature.
/// AWS SQS only offers a 15-minute send-time delay; GCP Pub/Sub has none.
/// </param>
/// <param name="SupportsRepeatablePeek">
/// Whether the provider's peek is safe to call on a short, repeating interval (auto-refresh,
/// Live Tail) without side effects that accumulate toward the entity's own redelivery limits.
/// Azure Service Bus peek is genuinely non-destructive. GCP Pub/Sub peek re-queues via
/// <c>ModifyAckDeadline(0)</c> with no consumer left blocked, matching the existing DLQ
/// background scan's polling cadence. AWS SQS has no non-destructive peek at all — every call
/// is a real receive that increments the message's <c>ReceiveCount</c>, so repeated polling
/// can push a message over its queue's <c>maxReceiveCount</c> and dead-letter it by accident.
/// </param>
/// <param name="Notes">
/// A short, human-readable explanation of the provider's constraints, suitable for a UI
/// tooltip or disabled-state message.
/// </param>
public sealed record ProviderCapabilities(
    bool SupportsMessageCounts,
    bool SupportsManualDeadLetter,
    bool SupportsPurge,
    bool SupportsScheduledMessages,
    bool SupportsRepeatablePeek,
    string Notes)
{
    /// <summary>Capabilities of Microsoft Azure Service Bus.</summary>
    public static readonly ProviderCapabilities Azure = new(
        SupportsMessageCounts: true,
        SupportsManualDeadLetter: true,
        SupportsPurge: false,
        SupportsScheduledMessages: true,
        SupportsRepeatablePeek: true,
        Notes: "Purge is not supported — the SDK has no reliable single-message delete by sequence number.");

    /// <summary>Capabilities of Amazon SQS/SNS.</summary>
    public static readonly ProviderCapabilities Aws = new(
        SupportsMessageCounts: true,
        SupportsManualDeadLetter: true,
        SupportsPurge: true,
        SupportsScheduledMessages: false,
        SupportsRepeatablePeek: false,
        Notes: "Scheduled messages are not supported — SQS only offers DelaySeconds (max 15 minutes) at send time. Repeated/live polling is also not supported — SQS has no non-destructive peek, so every call is a receive that counts toward the queue's maxReceiveCount.");

    /// <summary>Capabilities of Google Cloud Pub/Sub.</summary>
    public static readonly ProviderCapabilities Gcp = new(
        SupportsMessageCounts: false,
        SupportsManualDeadLetter: false,
        SupportsPurge: true,
        SupportsScheduledMessages: false,
        SupportsRepeatablePeek: true,
        Notes: "Message counts and manual dead-lettering are not supported — Pub/Sub has no count API and dead-lettering is policy-driven via MaxDeliveryAttempts. Scheduled messages are not supported either.");
}
