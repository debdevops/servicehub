using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Looks in one namespace's dead-letter queues now, because a person asked — and records what it finds in the
/// same durable list the DLQ monitor keeps, so every message can be opened, read and replayed.
/// </summary>
/// <remarks>
/// This is how clouds without a repeatable peek (AWS SQS, Google Pub/Sub) show their dead letters: ServiceHub
/// never looks there on a timer, because each look counts as a delivery attempt, but a person may choose to.
/// 4.0.0 did the same by browsing on demand; the difference is that what is seen is kept.
/// </remarks>
public interface IDeadLetterLook
{
    /// <summary>Looks once. Never throws for a cloud failure; the result says what happened.</summary>
    Task<DeadLetterLookResult> LookNowAsync(Namespace ns, CancellationToken cancellationToken);
}

/// <summary>What one look found.</summary>
/// <param name="Outcome"><c>looked</c>, <c>busy</c> (a look at this namespace is already running) or <c>failed</c>.</param>
/// <param name="QueuesExamined">Queues and subscriptions whose dead-letter queue was read.</param>
/// <param name="NewMessages">Dead letters recorded for the first time.</param>
/// <param name="Resolved">Recorded dead letters now confirmed gone.</param>
/// <param name="Unconfirmed">Queues that could not be fully read; their records were left as they were.</param>
/// <param name="CountsAsDeliveryAttempt">True where looking was a receive (no repeatable peek on this cloud).</param>
/// <param name="Reason">One plain sentence when the look did not happen.</param>
/// <param name="LookedAtUtc">When it looked.</param>
public sealed record DeadLetterLookResult(
    string Outcome,
    int QueuesExamined,
    int NewMessages,
    int Resolved,
    int Unconfirmed,
    bool CountsAsDeliveryAttempt,
    string? Reason,
    DateTimeOffset LookedAtUtc);
