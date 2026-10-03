using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Results;

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

    /// <summary>
    /// Sends one message and returns the id the cloud assigned it, or <see langword="null"/> when this sender
    /// cannot say. The DLQ observer's liveness canary needs it: the observer logs a message under that id.
    /// The default sends and reports no id, which a caller must read as "cannot be tracked", never as success of tracking.
    /// </summary>
    async Task<Result<string?>> SendReturningProviderIdAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        var sent = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        return sent.IsSuccess ? Result.Success<string?>(null) : Result.Failure<string?>(sent.Error);
    }
}
