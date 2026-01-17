using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Interface for sending messages to Azure Service Bus queues and topics.
/// </summary>
public interface IMessageSender
{
    /// <summary>
    /// Sends a message to a queue or topic.
    /// </summary>
    /// <param name="request">The send message request containing all message details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure of the send operation.</returns>
    Task<Result> SendAsync(SendMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends multiple messages to a queue or topic in a batch.
    /// </summary>
    /// <param name="requests">The collection of send message requests.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure of the batch send operation.</returns>
    Task<Result> SendBatchAsync(IEnumerable<SendMessageRequest> requests, CancellationToken cancellationToken = default);
}
