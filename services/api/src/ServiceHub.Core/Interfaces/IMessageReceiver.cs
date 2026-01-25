using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Interface for receiving/peeking messages from Azure Service Bus queues and subscriptions.
/// </summary>
public interface IMessageReceiver
{
    /// <summary>
    /// Peeks messages from a queue or subscription without removing them.
    /// </summary>
    /// <param name="request">The get messages request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the peeked messages.</returns>
    Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Peeks messages from the dead-letter queue.
    /// </summary>
    /// <param name="request">The get messages request with FromDeadLetter set to true.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the peeked dead-letter messages.</returns>
    Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the count of messages in a queue or subscription.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="entityName">The queue or topic name.</param>
    /// <param name="subscriptionName">Optional subscription name for topics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the message count.</returns>
    Task<Result<long>> GetMessageCountAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Receives a message and moves it to the dead-letter queue.
    /// This is used for testing DLQ functionality.
    /// </summary>
    /// <param name="request">The dead-letter request containing namespace, entity, and reason.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result<int>> DeadLetterMessagesAsync(DeadLetterRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Replays a message from the dead-letter queue back to the main queue.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="entityName">The queue or topic name.</param>
    /// <param name="subscriptionName">Optional subscription name for topics.</param>
    /// <param name="sequenceNumber">The sequence number of the message to replay.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> ReplayMessageAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Purges (permanently deletes) a message.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="entityName">The queue or topic name.</param>
    /// <param name="subscriptionName">Optional subscription name for topics.</param>
    /// <param name="sequenceNumber">The sequence number of the message to purge.</param>
    /// <param name="fromDeadLetter">Whether to purge from the dead-letter queue.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> PurgeMessageAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        bool fromDeadLetter,
        CancellationToken cancellationToken = default);
}
