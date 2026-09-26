using ServiceHub.Core.Events.Payloads;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Sends an escalation to every channel a person set up (units 5.5, 6.3) — Slack, Teams, generic webhooks — plus the one the
/// server's configuration names, if any. Best-effort: a channel failing is recorded on that channel and never fails the
/// escalation.
/// </summary>
public interface IEscalationDelivery
{
    /// <summary>Delivers to the owner's enabled channels. Never throws for a delivery failure.</summary>
    Task DeliverAsync(EscalationRaisedPayload escalation, string ownerId, CancellationToken cancellationToken);

    /// <summary>Sends one clearly-marked test message to one channel and says how it went.</summary>
    Task<(bool Delivered, string? Error)> SendTestAsync(Guid channelId, string ownerId, CancellationToken cancellationToken);
}
