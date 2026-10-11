using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Answers, for one replay, whether the replayed message came back to the dead-letter queue — for a cloud whose ordinary
/// peek cannot prove that (<c>ProviderCapabilities.CanProveDlqAbsence</c> is false). One implementation per such cloud,
/// each built on something that sees the WHOLE dead-letter queue (ADR-0018).
/// </summary>
/// <remarks>
/// The rule every implementation keeps: the answer is a fact or it is <see cref="DeadLetterReturnOutcome.CannotTell"/>.
/// There is no confidence score and no "probably". An expected problem (a queue that cannot be listed to the end, a
/// second reader, a missing permission) is never thrown — it is <c>CannotTell</c> with a reason.
/// </remarks>
public interface IDeadLetterReturnCheck
{
    /// <summary>The cloud this check serves.</summary>
    CloudProviderType Provider { get; }

    /// <summary>
    /// Whether a person must name something for this check to work (a subscription ServiceHub owns), or it needs nothing
    /// set up (it reads the queue itself). Lets the API validate without knowing which cloud it is talking about.
    /// </summary>
    bool NeedsObserverReference { get; }

    /// <summary>Why <paramref name="observerReference"/> cannot be used, or null if it can. Called before it is saved.</summary>
    string? ValidateObserverReference(string? observerReference);

    /// <summary>A cheap check that the view is usable right now. It records nothing itself.</summary>
    Task<DeadLetterViewHealth> CheckHealthAsync(Namespace ns, DlqObserverAttestation attestation, CancellationToken cancellationToken = default);

    /// <summary>The verdict for one replay.</summary>
    /// <param name="ns">The namespace the replay was made in.</param>
    /// <param name="attestation">That namespace's live attestation.</param>
    /// <param name="entry">The replay's ledger entry, still being watched.</param>
    /// <param name="liveSince">Since when this view has been continuously confirmed; null if that is not known.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    Task<DeadLetterReturnVerdict> CheckAsync(
        Namespace ns, DlqObserverAttestation attestation, RecoveryLedgerEntry entry, DateTimeOffset? liveSince, CancellationToken cancellationToken = default);
}

/// <summary>What a whole view of the dead-letter queue says about one replay.</summary>
public enum DeadLetterReturnOutcome
{
    /// <summary>The whole queue was seen and the replayed message is not in it.</summary>
    NotReturned = 0,

    /// <summary>The replayed message is back in the dead-letter queue.</summary>
    Returned = 1,

    /// <summary>The view could not prove it either way. Never treated as "not returned".</summary>
    CannotTell = 2,

    /// <summary>The message has not yet had time to fail and be dead-lettered again; ask later.</summary>
    TooEarly = 3,
}

/// <summary>The verdict and, where it is not a plain answer, why.</summary>
/// <param name="Outcome">The verdict.</param>
/// <param name="Reason">A short upper-case code (for example <c>FIFO_QUEUE</c>), or null.</param>
public sealed record DeadLetterReturnVerdict(DeadLetterReturnOutcome Outcome, string? Reason = null)
{
    /// <summary>The whole queue was seen without the message.</summary>
    public static readonly DeadLetterReturnVerdict NotReturned = new(DeadLetterReturnOutcome.NotReturned);

    /// <summary>The message is back.</summary>
    public static readonly DeadLetterReturnVerdict Returned = new(DeadLetterReturnOutcome.Returned);

    /// <summary>Ask again later.</summary>
    public static readonly DeadLetterReturnVerdict TooEarly = new(DeadLetterReturnOutcome.TooEarly);

    /// <summary>It could not be proven, and why.</summary>
    public static DeadLetterReturnVerdict CannotTell(string reason) => new(DeadLetterReturnOutcome.CannotTell, reason);
}

/// <summary>Whether a view of the dead-letter queue is usable right now.</summary>
/// <param name="Healthy">True when it is.</param>
/// <param name="Reason">A short upper-case code when it is not.</param>
/// <param name="Lost">
/// True when what the view depended on is gone (the subscription was deleted, or reads another topic): anything it saw
/// before can no longer be relied on, so "live since" starts again.
/// </param>
public sealed record DeadLetterViewHealth(bool Healthy, string? Reason = null, bool Lost = false);

/// <summary>
/// Optional: a check that can show, on request, whether it can see one dead-letter queue in full right now. It is what
/// "Check now" calls, and how the claim "this scan is complete" is tested against a real queue before it is relied on.
/// </summary>
public interface IDeadLetterWholeViewProbe
{
    /// <summary>Looks at <paramref name="entityName"/>'s dead-letter queue once and says what it saw. Changes nothing.</summary>
    Task<DeadLetterViewProbe> ProbeAsync(Namespace ns, string entityName, CancellationToken cancellationToken = default);
}

/// <summary>What one look at a dead-letter queue found.</summary>
/// <param name="Complete">True only when the look proved it saw every message.</param>
/// <param name="Reason">Why it is not complete, as a short upper-case code.</param>
/// <param name="Count">How many messages were seen. "All of them" only when <paramref name="Complete"/> is true.</param>
public sealed record DeadLetterViewProbe(bool Complete, string? Reason, int Count);
