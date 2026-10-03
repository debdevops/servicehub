using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// The information an <see cref="Interfaces.IWebhookMessageFormatter"/> needs to render an
/// insight-detected alert (roadmap §5, I5 — "Push"). Deliberately separate from
/// <see cref="Events.Payloads.InsightDetectedPayload"/> (the platform-event envelope payload) so
/// formatters don't depend on the event-bus shape — this is the notifier's own, stable input
/// contract.
/// </summary>
public sealed record InsightDetectedNotification(
    InsightKind Kind,
    Guid FindingId,
    Guid? NamespaceId,
    string? NamespaceName,
    string? EntityName,
    string Description,
    int Severity,
    DateTimeOffset DetectedAtUtc,
    string? InvestigateUrl);
