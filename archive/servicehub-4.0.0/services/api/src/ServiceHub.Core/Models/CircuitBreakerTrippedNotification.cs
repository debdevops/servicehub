namespace ServiceHub.Core.Models;

/// <summary>
/// The information an <see cref="Interfaces.IWebhookMessageFormatter"/> needs to render a
/// success-rate circuit breaker trip alert. Deliberately separate from
/// <see cref="Events.Payloads.AutoReplayRuleCircuitBreakerTrippedPayload"/> (the platform-event
/// envelope payload) so formatters don't depend on the event-bus shape — this is the notifier's
/// own, stable input contract.
/// </summary>
public sealed record CircuitBreakerTrippedNotification(
    long RuleId,
    string RuleName,
    int SampleSize,
    double VerifiedSuccessRate,
    DateTimeOffset TrippedAtUtc,
    string? InvestigateUrl);
