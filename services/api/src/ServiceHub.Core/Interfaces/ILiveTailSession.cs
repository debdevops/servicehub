using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// A single Live Tail viewing session scoped to one queue or subscription. Repeatedly
/// peeks the entity and yields only messages not already seen by this session — "tail -f"
/// semantics: the first poll seeds what's already there without yielding it, subsequent
/// polls yield genuinely new arrivals. State (which messages have been seen) lives only in
/// the session instance, for the lifetime of one connection; nothing is persisted.
/// </summary>
public interface ILiveTailSession
{
    /// <summary>
    /// Peeks the entity once and returns any messages not seen by a previous call on this
    /// session. Returns an empty list (not a failure) when nothing new has arrived.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IReadOnlyList<Message>>> PollNextAsync(CancellationToken cancellationToken = default);
}

/// <summary>Creates <see cref="ILiveTailSession"/> instances scoped to one entity.</summary>
public interface ILiveTailSessionFactory
{
    /// <summary>
    /// Creates a new Live Tail session for the given queue or subscription.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="entityName">The queue or topic name.</param>
    /// <param name="subscriptionName">Optional subscription name for topic subscriptions.</param>
    /// <param name="fromDeadLetter">Whether to tail the dead-letter queue instead of the active entity.</param>
    ILiveTailSession Create(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        bool fromDeadLetter);
}
