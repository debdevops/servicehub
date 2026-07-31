namespace ServiceHub.Core.Models;

/// <summary>
/// The information an <see cref="Interfaces.IWebhookMessageFormatter"/> needs to render a DLQ
/// spike alert. Deliberately separate from <see cref="Events.Payloads.DlqSpikeDetectedPayload"/>
/// (the platform-event envelope payload) so formatters don't depend on the event-bus shape —
/// this is the notifier's own, stable input contract.
/// </summary>
public sealed record DlqSpikeNotification(
    Guid NamespaceId,
    string NamespaceName,
    int NewMessageCount,
    int Threshold,
    DateTimeOffset DetectedAtUtc,
    string? InvestigateUrl);
