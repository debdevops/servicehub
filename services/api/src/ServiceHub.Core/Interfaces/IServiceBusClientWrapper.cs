using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Wrapper interface for Service Bus client operations.
/// Provides an abstraction over the Azure Service Bus SDK.
/// </summary>
public interface IServiceBusClientWrapper : IAsyncDisposable
{
    /// <summary>
    /// Gets the namespace identifier this client is associated with.
    /// </summary>
    Guid NamespaceId { get; }

    /// <summary>
    /// Gets a value indicating whether the client is closed.
    /// </summary>
    bool IsClosed { get; }

    /// <summary>
    /// Sends a message to a queue or topic.
    /// </summary>
    /// <param name="request">The send message request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Peeks messages from a queue or subscription without removing them.
    /// </summary>
    /// <param name="request">The get messages request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the peeked messages.</returns>
    Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(GetMessagesRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests the connection to the Service Bus namespace.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating whether the connection is healthy.</returns>
    Task<Result<bool>> TestConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all queues in the namespace.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the list of queue runtime properties.</returns>
    Task<Result<IReadOnlyList<QueueRuntimePropertiesDto>>> GetQueuesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific queue by name.
    /// </summary>
    /// <param name="queueName">The queue name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the queue runtime properties.</returns>
    Task<Result<QueueRuntimePropertiesDto>> GetQueueAsync(string queueName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all topics in the namespace.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the list of topic runtime properties.</returns>
    Task<Result<IReadOnlyList<TopicRuntimePropertiesDto>>> GetTopicsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific topic by name.
    /// </summary>
    /// <param name="topicName">The topic name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the topic runtime properties.</returns>
    Task<Result<TopicRuntimePropertiesDto>> GetTopicAsync(string topicName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all subscriptions for a topic.
    /// </summary>
    /// <param name="topicName">The topic name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the list of subscription runtime properties.</returns>
    Task<Result<IReadOnlyList<SubscriptionRuntimePropertiesDto>>> GetSubscriptionsAsync(string topicName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific subscription by name.
    /// </summary>
    /// <param name="topicName">The topic name.</param>
    /// <param name="subscriptionName">The subscription name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the subscription runtime properties.</returns>
    Task<Result<SubscriptionRuntimePropertiesDto>> GetSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken = default);
}
