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
/// Azure Service Bus peek is genuinely non-destructive. AWS SQS and GCP Pub/Sub both have no
/// non-destructive peek at all: every SQS receive increments the message's <c>ReceiveCount</c>,
/// and every Pub/Sub pull-then-<c>ModifyAckDeadline(0)</c> still counts as a delivery attempt
/// toward the subscription's <c>MaxDeliveryAttempts</c> dead-letter policy even though it
/// unblocks the next consumer immediately — so on both providers, repeated polling (Live Tail,
/// background DLQ scanning) can push a message over its redelivery limit and dead-letter it by
/// accident, purely from being watched.
/// </param>
/// <param name="Notes">
/// A short, human-readable explanation of the provider's constraints, suitable for a UI
/// tooltip or disabled-state message.
/// </param>
/// <param name="SupportsRecoveryMarker">
/// Whether the provider's message envelope can carry the <c>x-servicehub-recovery-id</c>
/// application property/attribute stamped on replay (see the Recovery Evidence Ledger's
/// verification model). True for all three providers at the envelope level; AWS SQS additionally
/// enforces a 10-attribute cap checked per-message at replay time — when a specific message is
/// already at the cap, the marker is not applied to that message even though the provider
/// generally supports it.
/// </param>
/// <param name="CanProveDlqAbsence">
/// Whether a background scan can establish that a replayed message did **not** return to the
/// DLQ. True only for Azure, whose peek can page up to 5,000 messages/entity/cycle
/// uncapped. False for AWS (background scanning is off by default, and every peek is a
/// destructive single 100-message receive) and GCP (a single 100-message batch per cycle, with
/// reconciliation skipped at the cap) — a capped sample can never prove a message is truly gone.
/// The Recovery Verification Worker consults this to decide between
/// <see cref="Enums.RecoveryEntryState.Recovered"/> and <see cref="Enums.RecoveryEntryState.Unverified"/>.
/// </param>
/// <param name="SupportsTopics">
/// Whether the provider has a publish/subscribe topic concept (<c>TopicsController</c>). True
/// for all three current providers — Azure Service Bus topics, SNS topics (AWS), and Pub/Sub
/// topics (GCP). Exists so a future queue-only provider can decline this without editing the
/// controller.
/// </param>
/// <param name="SupportsSubscriptions">
/// Whether the provider has a topic-subscription concept (<c>SubscriptionsController</c>). True
/// for all three current providers — Azure Service Bus subscriptions, SNS subscriptions (AWS),
/// and Pub/Sub subscriptions (GCP).
/// </param>
public sealed record ProviderCapabilities(
    bool SupportsMessageCounts,
    bool SupportsManualDeadLetter,
    bool SupportsPurge,
    bool SupportsScheduledMessages,
    bool SupportsRepeatablePeek,
    string Notes,
    bool SupportsRecoveryMarker,
    bool CanProveDlqAbsence,
    bool SupportsTopics = true,
    bool SupportsSubscriptions = true)
{
    /// <summary>Capabilities of Microsoft Azure Service Bus.</summary>
    public static readonly ProviderCapabilities Azure = new(
        SupportsMessageCounts: true,
        SupportsManualDeadLetter: true,
        SupportsPurge: false,
        SupportsScheduledMessages: true,
        SupportsRepeatablePeek: true,
        Notes: "Purge is not supported — the SDK has no reliable single-message delete by sequence number.",
        SupportsRecoveryMarker: true,
        CanProveDlqAbsence: true);

    /// <summary>Capabilities of Amazon SQS/SNS.</summary>
    public static readonly ProviderCapabilities Aws = new(
        SupportsMessageCounts: true,
        SupportsManualDeadLetter: true,
        SupportsPurge: true,
        SupportsScheduledMessages: false,
        SupportsRepeatablePeek: false,
        Notes: "Scheduled messages are not supported — SQS only offers DelaySeconds (max 15 minutes) at send time. Repeated/live polling is also not supported — SQS has no non-destructive peek, so every call is a receive that counts toward the queue's maxReceiveCount.",
        SupportsRecoveryMarker: true,
        CanProveDlqAbsence: false);

    /// <summary>Capabilities of Google Cloud Pub/Sub.</summary>
    public static readonly ProviderCapabilities Gcp = new(
        SupportsMessageCounts: false,
        SupportsManualDeadLetter: false,
        SupportsPurge: true,
        SupportsScheduledMessages: false,
        SupportsRepeatablePeek: false,
        Notes: "Message counts and manual dead-lettering are not supported — Pub/Sub has no count API and dead-lettering is policy-driven via MaxDeliveryAttempts. Scheduled messages are not supported either. Repeated/live polling is also not supported — every pull-then-release still counts as a delivery attempt toward the subscription's MaxDeliveryAttempts, so watching a message repeatedly can dead-letter it by accident.",
        SupportsRecoveryMarker: true,
        CanProveDlqAbsence: false);

    /// <summary>
    /// Resolves the capabilities preset for a given provider type. This is the single place
    /// that maps <see cref="Enums.CloudProviderType"/> to its <see cref="ProviderCapabilities"/>
    /// preset for callers that only have the enum value (e.g. before a provider is registered
    /// with <see cref="Interfaces.ICloudProviderRouter"/>).
    /// </summary>
    public static ProviderCapabilities For(Enums.CloudProviderType provider) => provider switch
    {
        Enums.CloudProviderType.Azure => Azure,
        Enums.CloudProviderType.Gcp => Gcp,
        _ => Aws
    };
}
