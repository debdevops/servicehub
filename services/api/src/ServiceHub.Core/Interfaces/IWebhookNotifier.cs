using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Sends webhook notifications for DLQ activity and bulk operation outcomes. Payload shape
/// (generic JSON, Slack, or Teams) is controlled by <c>WebhookOptions.Format</c> and delegated
/// to an <see cref="IWebhookMessageFormatter"/> — this interface itself never changes to add a
/// new destination format.
/// </summary>
public interface IWebhookNotifier
{
    /// <summary>
    /// Notifies external systems about a DLQ spike in the given namespace.
    /// </summary>
    /// <param name="namespaceId">The namespace that was scanned.</param>
    /// <param name="namespaceName">Human-readable namespace name.</param>
    /// <param name="newMessageCount">Number of new DLQ messages detected in this scan cycle.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<Result> NotifyDlqSpikeAsync(
        Guid namespaceId,
        string namespaceName,
        int newMessageCount,
        CancellationToken cancellationToken = default);


    /// <summary>
    /// Notifies external systems that a failure signature's autonomy grant transitioned
    /// (promotion or demotion). Like the bulk-operation alert, this is not threshold-gated or
    /// cooled-down — each transition is a single, meaningful, evidence-derived event worth
    /// reporting once.
    /// </summary>
    /// <param name="signatureHash">The failure signature whose grant transitioned.</param>
    /// <param name="previousLevel">The level before this transition.</param>
    /// <param name="newLevel">The level after this transition.</param>
    /// <param name="reason">Human-readable reason for the transition.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<Result> NotifyAutonomyTransitionAsync(
        string signatureHash,
        AutonomyLevel previousLevel,
        AutonomyLevel newLevel,
        string reason,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies external systems that the success-rate circuit breaker automatically disabled an
    /// auto-replay rule. Not threshold-gated or cooled-down — a trip is itself already a rare,
    /// deliberate protective action, worth reporting once.
    /// </summary>
    /// <param name="ruleId">Identifier of the disabled auto-replay rule.</param>
    /// <param name="ruleName">Display name of the disabled rule.</param>
    /// <param name="sampleSize">How many recent verified outcomes the trip was computed from.</param>
    /// <param name="verifiedSuccessRate">The verified success rate that fell below the floor.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<Result> NotifyCircuitBreakerTrippedAsync(
        long ruleId,
        string ruleName,
        int sampleSize,
        double verifiedSuccessRate,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Notifies external systems that a detection pillar (Investigate/Prevent/Correlate)
    /// produced a finding worth surfacing without waiting for an operator to open the app
    /// (roadmap §5, I5 — "Push"). Not threshold-gated or cooled-down here — the caller (a
    /// detection worker) only invokes this for findings that already cleared its own
    /// significance threshold, so every call is worth reporting once.
    /// </summary>
    /// <param name="kind">Which detection pillar produced the finding.</param>
    /// <param name="findingId">The finding's own identifier.</param>
    /// <param name="namespaceId">The namespace the finding concerns, if single-namespace-scoped.</param>
    /// <param name="namespaceName">Human-readable namespace name, if applicable.</param>
    /// <param name="entityName">The queue/topic/subscription the finding concerns, if applicable.</param>
    /// <param name="description">Human-readable description of the finding.</param>
    /// <param name="severity">The finding's severity (0-100, where 100 is most severe).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Success or failure result.</returns>
    Task<Result> NotifyInsightDetectedAsync(
        InsightKind kind,
        Guid findingId,
        Guid? namespaceId,
        string? namespaceName,
        string? entityName,
        string description,
        int severity,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one escalation — what stopped, where, why, what to do (unit 5.5). No threshold and no cooldown: each escalation is
    /// one decision a person owes, and it is raised once. Best-effort: a failure here never fails the escalation.
    /// </summary>
    Task<Result> NotifyEscalationAsync(
        string kind, string reasonCode, string reason, string? namespaceName, string? provider, string? entity, Guid? entryId, string? agentId,
        CancellationToken cancellationToken = default);
}
