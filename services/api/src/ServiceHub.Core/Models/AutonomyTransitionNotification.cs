using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// The information an <see cref="Interfaces.IWebhookMessageFormatter"/> needs to render an
/// autonomy grant transition (promotion or demotion) alert. Deliberately separate from
/// <see cref="Events.Payloads.AutonomyGrantTransitionedPayload"/> (the platform-event envelope
/// payload) so formatters don't depend on the event-bus shape — this is the notifier's own,
/// stable input contract.
/// </summary>
public sealed record AutonomyTransitionNotification(
    string SignatureHash,
    AutonomyLevel PreviousLevel,
    AutonomyLevel NewLevel,
    string Reason,
    DateTimeOffset TransitionedAtUtc,
    string? InvestigateUrl);
