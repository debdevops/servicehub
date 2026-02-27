using System.Text.Json;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.ServiceBus;

/// <summary>
/// Wrapper around Azure Service Bus client providing high-level operations.
/// </summary>
public sealed class ServiceBusClientWrapper : IServiceBusClientWrapper
{
    private readonly ServiceBusClient _client;
    private readonly ILogger<ServiceBusClientWrapper> _logger;
    private volatile bool _disposed;

    // CRITICAL FIX: Cache admin client to prevent per-request creation and socket exhaustion
    private ServiceBusAdministrationClient? _adminClient;
    private readonly SemaphoreSlim _adminClientLock = new(1, 1);

    /// <inheritdoc/>
    public Guid NamespaceId { get; }

    /// <inheritdoc/>
    public bool IsClosed => _client.IsClosed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusClientWrapper"/> class.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="client">The underlying Service Bus client.</param>
    /// <param name="logger">The logger instance.</param>
    public ServiceBusClientWrapper(
        Guid namespaceId,
        ServiceBusClient client,
        ILogger<ServiceBusClientWrapper> logger)
    {
        NamespaceId = namespaceId;
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result> SendMessageAsync(SendMessageRequest request, CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            return Result.Failure(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed."));
        }

        if (string.IsNullOrWhiteSpace(request.EntityName))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Message.QueueNameRequired,
                "Queue or topic name is required."));
        }

        if (string.IsNullOrWhiteSpace(request.Body))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Message.BodyRequired,
                "Message body is required."));
        }

        ServiceBusSender? sender = null;
        try
        {
            sender = _client.CreateSender(request.EntityName);
            var message = CreateServiceBusMessage(request);

            await sender.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            _logger.LogDebug(
                "Message sent to {EntityName} in namespace {NamespaceId}",
                request.EntityName,
                NamespaceId);

            return Result.Success();
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.LogWarning(ex,
                "Entity {EntityName} not found in namespace {NamespaceId}",
                request.EntityName,
                NamespaceId);

            return Result.Failure(Error.NotFound(
                ErrorCodes.Queue.NotFound,
                $"The queue or topic '{request.EntityName}' was not found."));
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessageSizeExceeded)
        {
            _logger.LogWarning(ex,
                "Message size exceeded for entity {EntityName}",
                request.EntityName);

            return Result.Failure(Error.Validation(
                ErrorCodes.Message.BodyTooLarge,
                "The message body exceeds the maximum allowed size."));
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex,
                "Service Bus error sending message to {EntityName}",
                request.EntityName);

            return Result.Failure(Error.ExternalService(
                ErrorCodes.Message.SendFailed,
                $"Failed to send message: {ex.Reason}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error sending message to {EntityName}",
                request.EntityName);

            return Result.Failure(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while sending the message."));
        }
        finally
        {
            if (sender != null)
            {
                await sender.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            return Result.Failure<IReadOnlyList<Message>>(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed."));
        }

        if (string.IsNullOrWhiteSpace(request.EntityName))
        {
            return Result.Failure<IReadOnlyList<Message>>(Error.Validation(
                ErrorCodes.Message.QueueNameRequired,
                "Queue or topic name is required."));
        }

        var maxMessages = Math.Clamp(request.MaxMessages, GetMessagesRequest.MinAllowedMessages, GetMessagesRequest.MaxAllowedMessages);

        ServiceBusReceiver? receiver = null;
        try
        {
            var entityPath = BuildEntityPath(request.EntityName, request.SubscriptionName, request.FromDeadLetter);
            receiver = CreateReceiver(request.EntityName, request.SubscriptionName, request.FromDeadLetter);

            IReadOnlyList<ServiceBusReceivedMessage> peekedMessages;

            if (request.FromSequenceNumber.HasValue)
            {
                peekedMessages = await receiver
                    .PeekMessagesAsync(maxMessages, request.FromSequenceNumber.Value, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                peekedMessages = await receiver
                    .PeekMessagesAsync(maxMessages, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
            }

            var messages = peekedMessages
                .Select(m => MapToMessage(m, request))
                .ToList();

            _logger.LogDebug(
                "Peeked {Count} messages from {EntityPath} in namespace {NamespaceId}",
                messages.Count,
                entityPath,
                NamespaceId);

            return Result.Success<IReadOnlyList<Message>>(messages);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.LogWarning(ex,
                "Entity {EntityName} not found in namespace {NamespaceId}",
                request.EntityName,
                NamespaceId);

            return Result.Failure<IReadOnlyList<Message>>(Error.NotFound(
                ErrorCodes.Queue.NotFound,
                $"The queue, topic, or subscription '{request.EntityName}' was not found."));
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex,
                "Service Bus error peeking messages from {EntityName}",
                request.EntityName);

            return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService(
                ErrorCodes.Message.ReceiveFailed,
                $"Failed to peek messages: {ex.Reason}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error peeking messages from {EntityName}",
                request.EntityName);

            return Result.Failure<IReadOnlyList<Message>>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while peeking messages."));
        }
        finally
        {
            if (receiver != null)
            {
                await receiver.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public Task<Result<bool>> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            return Task.FromResult(Result.Failure<bool>(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed.")));
        }

        try
        {
            // Use the connection string to create an administration client for health check
            // This is a lightweight operation that validates connectivity
            var fullyQualifiedNamespace = _client.FullyQualifiedNamespace;

            if (string.IsNullOrEmpty(fullyQualifiedNamespace))
            {
                return Task.FromResult(Result.Failure<bool>(Error.ExternalService(
                    ErrorCodes.Namespace.ConnectionFailed,
                    "Unable to determine the Service Bus namespace.")));
            }

            _logger.LogDebug(
                "Connection test successful for namespace {NamespaceId} ({Namespace})",
                NamespaceId,
                fullyQualifiedNamespace);

            return Task.FromResult(Result.Success(true));
        }
        catch (ServiceBusException ex)
        {
            _logger.LogWarning(ex,
                "Connection test failed for namespace {NamespaceId}",
                NamespaceId);

            return Task.FromResult(Result.Failure<bool>(Error.ExternalService(
                ErrorCodes.Namespace.ConnectionFailed,
                $"Connection test failed: {ex.Reason}")));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error during connection test for namespace {NamespaceId}",
                NamespaceId);

            return Task.FromResult(Result.Failure<bool>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred during the connection test.")));
        }
    }

    /// <summary>
    /// Gets or creates the cached ServiceBusAdministrationClient.
    /// CRITICAL FIX: Ensures only ONE admin client per namespace to prevent socket exhaustion.
    /// </summary>
    private async ValueTask<ServiceBusAdministrationClient> GetOrCreateAdminClientAsync()
    {
        // Fast path: return existing client without locking
        if (_adminClient != null)
        {
            return _adminClient;
        }

        // Slow path: create client with lock
        await _adminClientLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_adminClient == null)
            {
                _adminClient = new ServiceBusAdministrationClient(
                    _client.FullyQualifiedNamespace,
                    new Azure.Identity.DefaultAzureCredential());

                _logger.LogDebug(
                    "Created ServiceBusAdministrationClient for namespace {NamespaceId}",
                    NamespaceId);
            }

            return _adminClient;
        }
        finally
        {
            _adminClientLock.Release();
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        try
        {
            await _client.DisposeAsync().ConfigureAwait(false);
            _logger.LogDebug("Disposed ServiceBusClient for namespace {NamespaceId}", NamespaceId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error disposing ServiceBusClient for namespace {NamespaceId}", NamespaceId);
        }

        // CRITICAL FIX: Dispose admin client to release resources
        _adminClientLock.Dispose();
    }

    /// <inheritdoc/>
    public async Task<int> DeadLetterMessagesAsync(
        string entityName,
        string? subscriptionName,
        int messageCount,
        string reason,
        string? errorDescription,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            _logger.LogWarning("Attempted to dead-letter messages on a disposed client");
            return 0;
        }

        ServiceBusReceiver? receiver = null;
        var deadLetteredCount = 0;

        try
        {
            // Create receiver for the entity (queue or subscription)
            var receiverOptions = new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock, // Required for dead-lettering
            };

            if (!string.IsNullOrEmpty(subscriptionName))
            {
                receiver = _client.CreateReceiver(entityName, subscriptionName, receiverOptions);
            }
            else
            {
                receiver = _client.CreateReceiver(entityName, receiverOptions);
            }

            // Receive messages and dead-letter them
            for (var i = 0; i < messageCount; i++)
            {
                var message = await receiver.ReceiveMessageAsync(
                    TimeSpan.FromSeconds(5),
                    cancellationToken).ConfigureAwait(false);

                if (message == null)
                {
                    _logger.LogDebug("No more messages to dead-letter in {EntityName}", entityName);
                    break;
                }

                await receiver.DeadLetterMessageAsync(
                    message,
                    reason,
                    errorDescription ?? $"Manually dead-lettered for testing purposes at {DateTime.UtcNow:O}",
                    cancellationToken).ConfigureAwait(false);

                deadLetteredCount++;

                _logger.LogDebug(
                    "Dead-lettered message {MessageId} from {EntityName} with reason: {Reason}",
                    message.MessageId,
                    entityName,
                    reason);
            }

            _logger.LogInformation(
                "Successfully dead-lettered {Count} messages from {EntityName} in namespace {NamespaceId}",
                deadLetteredCount,
                entityName,
                NamespaceId);

            return deadLetteredCount;
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.LogWarning(ex,
                "Entity {EntityName} not found when attempting to dead-letter messages",
                entityName);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Error dead-lettering messages from {EntityName}",
                entityName);
            throw;
        }
        finally
        {
            if (receiver != null)
            {
                await receiver.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<QueueRuntimePropertiesDto>>> GetQueuesAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            return Result.Failure<IReadOnlyList<QueueRuntimePropertiesDto>>(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed."));
        }

        try
        {
            // CRITICAL FIX: Reuse cached admin client instead of creating new one per request
            var adminClient = await GetOrCreateAdminClientAsync().ConfigureAwait(false);
            var queues = new List<QueueRuntimePropertiesDto>();

            await foreach (var queue in adminClient.GetQueuesRuntimePropertiesAsync(cancellationToken))
            {
                queues.Add(MapToQueueDto(queue));
            }

            _logger.LogDebug(
                "Retrieved {Count} queues from namespace {NamespaceId}",
                queues.Count,
                NamespaceId);

            return Result.Success<IReadOnlyList<QueueRuntimePropertiesDto>>(queues);
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex, "Service Bus error getting queues for namespace {NamespaceId}", NamespaceId);
            return Result.Failure<IReadOnlyList<QueueRuntimePropertiesDto>>(Error.ExternalService(
                ErrorCodes.Queue.ListFailed,
                $"Failed to list queues: {ex.Reason}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting queues for namespace {NamespaceId}", NamespaceId);
            return Result.Failure<IReadOnlyList<QueueRuntimePropertiesDto>>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while listing queues."));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<QueueRuntimePropertiesDto>> GetQueueAsync(string queueName, CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            return Result.Failure<QueueRuntimePropertiesDto>(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed."));
        }

        try
        {
            // CRITICAL FIX: Reuse cached admin client instead of creating new one per request
            var adminClient = await GetOrCreateAdminClientAsync().ConfigureAwait(false);
            var queueResponse = await adminClient.GetQueueRuntimePropertiesAsync(queueName, cancellationToken);
            var propsResponse = await adminClient.GetQueueAsync(queueName, cancellationToken);

            _logger.LogDebug(
                "Retrieved queue {QueueName} from namespace {NamespaceId}",
                queueName,
                NamespaceId);

            return Result.Success(MapToQueueDto(queueResponse.Value, propsResponse.Value));
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.LogWarning(ex, "Queue {QueueName} not found in namespace {NamespaceId}", queueName, NamespaceId);
            return Result.Failure<QueueRuntimePropertiesDto>(Error.NotFound(
                ErrorCodes.Queue.NotFound,
                $"Queue '{queueName}' was not found."));
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex, "Service Bus error getting queue {QueueName} for namespace {NamespaceId}", queueName, NamespaceId);
            return Result.Failure<QueueRuntimePropertiesDto>(Error.ExternalService(
                ErrorCodes.Queue.GetFailed,
                $"Failed to get queue: {ex.Reason}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting queue {QueueName} for namespace {NamespaceId}", queueName, NamespaceId);
            return Result.Failure<QueueRuntimePropertiesDto>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while getting queue."));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<TopicRuntimePropertiesDto>>> GetTopicsAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            return Result.Failure<IReadOnlyList<TopicRuntimePropertiesDto>>(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed."));
        }

        try
        {
            // CRITICAL FIX: Reuse cached admin client instead of creating new one per request
            var adminClient = await GetOrCreateAdminClientAsync().ConfigureAwait(false);
            var topics = new List<TopicRuntimePropertiesDto>();

            await foreach (var topic in adminClient.GetTopicsRuntimePropertiesAsync(cancellationToken))
            {
                topics.Add(MapToTopicDto(topic));
            }

            _logger.LogDebug(
                "Retrieved {Count} topics from namespace {NamespaceId}",
                topics.Count,
                NamespaceId);

            return Result.Success<IReadOnlyList<TopicRuntimePropertiesDto>>(topics);
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex, "Service Bus error getting topics for namespace {NamespaceId}", NamespaceId);
            return Result.Failure<IReadOnlyList<TopicRuntimePropertiesDto>>(Error.ExternalService(
                ErrorCodes.Topic.ListFailed,
                $"Failed to list topics: {ex.Reason}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting topics for namespace {NamespaceId}", NamespaceId);
            return Result.Failure<IReadOnlyList<TopicRuntimePropertiesDto>>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while listing topics."));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<TopicRuntimePropertiesDto>> GetTopicAsync(string topicName, CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            return Result.Failure<TopicRuntimePropertiesDto>(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed."));
        }

        try
        {
            // CRITICAL FIX: Reuse cached admin client instead of creating new one per request
            var adminClient = await GetOrCreateAdminClientAsync().ConfigureAwait(false);
            var topicResponse = await adminClient.GetTopicRuntimePropertiesAsync(topicName, cancellationToken);
            var propsResponse = await adminClient.GetTopicAsync(topicName, cancellationToken);

            _logger.LogDebug(
                "Retrieved topic {TopicName} from namespace {NamespaceId}",
                topicName,
                NamespaceId);

            return Result.Success(MapToTopicDto(topicResponse.Value, propsResponse.Value));
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.LogWarning(ex, "Topic {TopicName} not found in namespace {NamespaceId}", topicName, NamespaceId);
            return Result.Failure<TopicRuntimePropertiesDto>(Error.NotFound(
                ErrorCodes.Topic.NotFound,
                $"Topic '{topicName}' was not found."));
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex, "Service Bus error getting topic {TopicName} for namespace {NamespaceId}", topicName, NamespaceId);
            return Result.Failure<TopicRuntimePropertiesDto>(Error.ExternalService(
                ErrorCodes.Topic.GetFailed,
                $"Failed to get topic: {ex.Reason}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting topic {TopicName} for namespace {NamespaceId}", topicName, NamespaceId);
            return Result.Failure<TopicRuntimePropertiesDto>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while getting topic."));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<SubscriptionRuntimePropertiesDto>>> GetSubscriptionsAsync(string topicName, CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            return Result.Failure<IReadOnlyList<SubscriptionRuntimePropertiesDto>>(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed."));
        }

        try
        {
            // CRITICAL FIX: Reuse cached admin client instead of creating new one per request
            var adminClient = await GetOrCreateAdminClientAsync().ConfigureAwait(false);
            var subscriptions = new List<SubscriptionRuntimePropertiesDto>();

            await foreach (var subscription in adminClient.GetSubscriptionsRuntimePropertiesAsync(topicName, cancellationToken))
            {
                subscriptions.Add(MapToSubscriptionDto(subscription));
            }

            _logger.LogDebug(
                "Retrieved {Count} subscriptions for topic {TopicName} from namespace {NamespaceId}",
                subscriptions.Count,
                topicName,
                NamespaceId);

            return Result.Success<IReadOnlyList<SubscriptionRuntimePropertiesDto>>(subscriptions);
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.LogWarning(ex, "Topic {TopicName} not found in namespace {NamespaceId}", topicName, NamespaceId);
            return Result.Failure<IReadOnlyList<SubscriptionRuntimePropertiesDto>>(Error.NotFound(
                ErrorCodes.Topic.NotFound,
                $"Topic '{topicName}' was not found."));
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex, "Service Bus error getting subscriptions for topic {TopicName} in namespace {NamespaceId}", topicName, NamespaceId);
            return Result.Failure<IReadOnlyList<SubscriptionRuntimePropertiesDto>>(Error.ExternalService(
                ErrorCodes.Subscription.ListFailed,
                $"Failed to list subscriptions: {ex.Reason}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting subscriptions for topic {TopicName} in namespace {NamespaceId}", topicName, NamespaceId);
            return Result.Failure<IReadOnlyList<SubscriptionRuntimePropertiesDto>>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while listing subscriptions."));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<SubscriptionRuntimePropertiesDto>> GetSubscriptionAsync(string topicName, string subscriptionName, CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            return Result.Failure<SubscriptionRuntimePropertiesDto>(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed."));
        }

        try
        {
            // CRITICAL FIX: Reuse cached admin client instead of creating new one per request
            var adminClient = await GetOrCreateAdminClientAsync().ConfigureAwait(false);
            var subscriptionResponse = await adminClient.GetSubscriptionRuntimePropertiesAsync(topicName, subscriptionName, cancellationToken);
            var propsResponse = await adminClient.GetSubscriptionAsync(topicName, subscriptionName, cancellationToken);

            _logger.LogDebug(
                "Retrieved subscription {SubscriptionName} for topic {TopicName} from namespace {NamespaceId}",
                subscriptionName,
                topicName,
                NamespaceId);

            return Result.Success(MapToSubscriptionDto(subscriptionResponse.Value, propsResponse.Value));
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.LogWarning(ex, "Subscription {SubscriptionName} for topic {TopicName} not found in namespace {NamespaceId}", subscriptionName, topicName, NamespaceId);
            return Result.Failure<SubscriptionRuntimePropertiesDto>(Error.NotFound(
                ErrorCodes.Subscription.NotFound,
                $"Subscription '{subscriptionName}' for topic '{topicName}' was not found."));
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex, "Service Bus error getting subscription {SubscriptionName} for topic {TopicName} in namespace {NamespaceId}", subscriptionName, topicName, NamespaceId);
            return Result.Failure<SubscriptionRuntimePropertiesDto>(Error.ExternalService(
                ErrorCodes.Subscription.GetFailed,
                $"Failed to get subscription: {ex.Reason}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting subscription {SubscriptionName} for topic {TopicName} in namespace {NamespaceId}", subscriptionName, topicName, NamespaceId);
            return Result.Failure<SubscriptionRuntimePropertiesDto>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while getting subscription."));
        }
    }

    private ServiceBusReceiver CreateReceiver(string entityName, string? subscriptionName, bool fromDeadLetter)
    {
        var options = new ServiceBusReceiverOptions
        {
            ReceiveMode = ServiceBusReceiveMode.PeekLock,
            PrefetchCount = 0
        };

        if (fromDeadLetter)
        {
            options.SubQueue = SubQueue.DeadLetter;
        }

        if (!string.IsNullOrWhiteSpace(subscriptionName))
        {
            return _client.CreateReceiver(entityName, subscriptionName, options);
        }

        return _client.CreateReceiver(entityName, options);
    }

    private static string BuildEntityPath(string entityName, string? subscriptionName, bool fromDeadLetter)
    {
        var path = entityName;

        if (!string.IsNullOrWhiteSpace(subscriptionName))
        {
            path = $"{entityName}/Subscriptions/{subscriptionName}";
        }

        if (fromDeadLetter)
        {
            path = $"{path}/$DeadLetterQueue";
        }

        return path;
    }

    private static ServiceBusMessage CreateServiceBusMessage(SendMessageRequest request)
    {
        var message = new ServiceBusMessage(request.Body);

        if (!string.IsNullOrWhiteSpace(request.ContentType))
        {
            message.ContentType = request.ContentType;
        }

        if (!string.IsNullOrWhiteSpace(request.CorrelationId))
        {
            message.CorrelationId = request.CorrelationId;
        }

        if (!string.IsNullOrWhiteSpace(request.SessionId))
        {
            message.SessionId = request.SessionId;
        }

        if (!string.IsNullOrWhiteSpace(request.PartitionKey))
        {
            message.PartitionKey = request.PartitionKey;
        }

        if (!string.IsNullOrWhiteSpace(request.Subject))
        {
            message.Subject = request.Subject;
        }

        if (!string.IsNullOrWhiteSpace(request.ReplyTo))
        {
            message.ReplyTo = request.ReplyTo;
        }

        if (!string.IsNullOrWhiteSpace(request.ReplyToSessionId))
        {
            message.ReplyToSessionId = request.ReplyToSessionId;
        }

        if (!string.IsNullOrWhiteSpace(request.To))
        {
            message.To = request.To;
        }

        if (request.TimeToLiveSeconds.HasValue && request.TimeToLiveSeconds.Value > 0)
        {
            message.TimeToLive = TimeSpan.FromSeconds(request.TimeToLiveSeconds.Value);
        }

        if (request.ScheduledEnqueueTimeUtc.HasValue)
        {
            message.ScheduledEnqueueTime = request.ScheduledEnqueueTimeUtc.Value;
        }

        if (request.ApplicationProperties is { Count: > 0 })
        {
            foreach (var (key, value) in request.ApplicationProperties)
            {
                var converted = ConvertApplicationPropertyValue(value);
                if (converted is not null)
                {
                    message.ApplicationProperties[key] = converted;
                }
            }
        }

        return message;
    }

    private static object? ConvertApplicationPropertyValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement jsonElement)
        {
            return jsonElement.ValueKind switch
            {
                JsonValueKind.String => jsonElement.GetString(),
                JsonValueKind.Number => jsonElement.TryGetInt64(out var longValue)
                    ? longValue
                    : jsonElement.TryGetDecimal(out var decimalValue)
                        ? decimalValue
                        : jsonElement.GetDouble(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                JsonValueKind.Undefined => null,
                _ => jsonElement.GetRawText()
            };
        }

        return value;
    }

    private Message MapToMessage(ServiceBusReceivedMessage sbMessage, GetMessagesRequest request)
    {
        var state = DetermineMessageState(sbMessage, request.FromDeadLetter);

        return new Message
        {
            MessageId = sbMessage.MessageId ?? Guid.NewGuid().ToString(),
            SequenceNumber = sbMessage.SequenceNumber,
            Body = sbMessage.Body?.ToString(),
            ContentType = sbMessage.ContentType,
            CorrelationId = sbMessage.CorrelationId,
            SessionId = sbMessage.SessionId,
            PartitionKey = sbMessage.PartitionKey,
            ReplyTo = sbMessage.ReplyTo,
            ReplyToSessionId = sbMessage.ReplyToSessionId,
            To = sbMessage.To,
            Subject = sbMessage.Subject,
            TimeToLive = sbMessage.TimeToLive,
            ScheduledEnqueueTime = sbMessage.ScheduledEnqueueTime != default ? sbMessage.ScheduledEnqueueTime : null,
            EnqueuedTime = sbMessage.EnqueuedTime,
            ExpiresAt = sbMessage.ExpiresAt != default ? sbMessage.ExpiresAt : null,
            LockedUntil = sbMessage.LockedUntil != default ? sbMessage.LockedUntil : null,
            LockToken = null, // Not available for peeked messages
            DeliveryCount = sbMessage.DeliveryCount,
            State = state,
            DeadLetterSource = sbMessage.DeadLetterSource,
            DeadLetterReason = sbMessage.DeadLetterReason,
            DeadLetterErrorDescription = sbMessage.DeadLetterErrorDescription,
            ApplicationProperties = sbMessage.ApplicationProperties?.Count > 0
                ? sbMessage.ApplicationProperties.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)
                : null,
            SizeInBytes = sbMessage.Body?.ToMemory().Length ?? 0,
            NamespaceId = request.NamespaceId,
            EntityName = request.EntityName,
            SubscriptionName = request.SubscriptionName,
            IsFromDeadLetter = request.FromDeadLetter,
            EnqueuedSequenceNumber = sbMessage.EnqueuedSequenceNumber
        };
    }

    private static MessageState DetermineMessageState(ServiceBusReceivedMessage message, bool isFromDeadLetter)
    {
        if (isFromDeadLetter)
        {
            return MessageState.DeadLettered;
        }

        if (message.State == ServiceBusMessageState.Deferred)
        {
            return MessageState.Deferred;
        }

        if (message.State == ServiceBusMessageState.Scheduled)
        {
            return MessageState.Scheduled;
        }

        return MessageState.Active;
    }

    private static QueueRuntimePropertiesDto MapToQueueDto(QueueRuntimeProperties runtime, QueueProperties? props = null)
    {
        return new QueueRuntimePropertiesDto(
            Name: runtime.Name,
            ActiveMessageCount: runtime.ActiveMessageCount,
            DeadLetterMessageCount: runtime.DeadLetterMessageCount,
            ScheduledMessageCount: runtime.ScheduledMessageCount,
            TransferMessageCount: runtime.TransferMessageCount,
            TransferDeadLetterMessageCount: runtime.TransferDeadLetterMessageCount,
            SizeInBytes: runtime.SizeInBytes,
            Status: props?.Status.ToString() ?? "Unknown",
            CreatedAt: runtime.CreatedAt,
            UpdatedAt: runtime.UpdatedAt,
            AccessedAt: runtime.AccessedAt,
            RequiresSession: props?.RequiresSession ?? false,
            RequiresDuplicateDetection: props?.RequiresDuplicateDetection ?? false,
            EnablePartitioning: props?.EnablePartitioning ?? false,
            EnableBatchedOperations: props?.EnableBatchedOperations ?? true,
            MaxSizeInMegabytes: props?.MaxSizeInMegabytes ?? 0,
            MaxDeliveryCount: props?.MaxDeliveryCount ?? 10,
            DefaultMessageTimeToLive: props?.DefaultMessageTimeToLive ?? TimeSpan.MaxValue,
            LockDuration: props?.LockDuration ?? TimeSpan.FromMinutes(1),
            AutoDeleteOnIdle: props?.AutoDeleteOnIdle ?? TimeSpan.MaxValue);
    }

    private static TopicRuntimePropertiesDto MapToTopicDto(TopicRuntimeProperties runtime, TopicProperties? props = null)
    {
        return new TopicRuntimePropertiesDto(
            Name: runtime.Name,
            SubscriptionCount: runtime.SubscriptionCount,
            SizeInBytes: runtime.SizeInBytes,
            Status: props?.Status.ToString() ?? "Unknown",
            CreatedAt: runtime.CreatedAt,
            UpdatedAt: runtime.UpdatedAt,
            AccessedAt: runtime.AccessedAt,
            RequiresDuplicateDetection: props?.RequiresDuplicateDetection ?? false,
            EnablePartitioning: props?.EnablePartitioning ?? false,
            EnableBatchedOperations: props?.EnableBatchedOperations ?? true,
            SupportOrdering: props?.SupportOrdering ?? false,
            MaxSizeInMegabytes: props?.MaxSizeInMegabytes ?? 0,
            DefaultMessageTimeToLive: props?.DefaultMessageTimeToLive ?? TimeSpan.MaxValue,
            AutoDeleteOnIdle: props?.AutoDeleteOnIdle ?? TimeSpan.MaxValue,
            DuplicateDetectionHistoryTimeWindow: props?.DuplicateDetectionHistoryTimeWindow ?? TimeSpan.FromMinutes(10));
    }

    private static SubscriptionRuntimePropertiesDto MapToSubscriptionDto(SubscriptionRuntimeProperties runtime, SubscriptionProperties? props = null)
    {
        return new SubscriptionRuntimePropertiesDto(
            Name: runtime.SubscriptionName,
            TopicName: runtime.TopicName,
            ActiveMessageCount: runtime.ActiveMessageCount,
            DeadLetterMessageCount: runtime.DeadLetterMessageCount,
            TransferMessageCount: runtime.TransferMessageCount,
            TransferDeadLetterMessageCount: runtime.TransferDeadLetterMessageCount,
            Status: props?.Status.ToString() ?? "Unknown",
            CreatedAt: runtime.CreatedAt,
            UpdatedAt: runtime.UpdatedAt,
            AccessedAt: runtime.AccessedAt,
            RequiresSession: props?.RequiresSession ?? false,
            EnableBatchedOperations: props?.EnableBatchedOperations ?? true,
            EnableDeadLetteringOnMessageExpiration: props?.DeadLetteringOnMessageExpiration ?? false,
            EnableDeadLetteringOnFilterEvaluationExceptions: props?.EnableDeadLetteringOnFilterEvaluationExceptions ?? false,
            MaxDeliveryCount: props?.MaxDeliveryCount ?? 10,
            DefaultMessageTimeToLive: props?.DefaultMessageTimeToLive ?? TimeSpan.MaxValue,
            LockDuration: props?.LockDuration ?? TimeSpan.FromMinutes(1),
            AutoDeleteOnIdle: props?.AutoDeleteOnIdle ?? TimeSpan.MaxValue,
            ForwardTo: props?.ForwardTo,
            ForwardDeadLetteredMessagesTo: props?.ForwardDeadLetteredMessagesTo);
    }

    /// <inheritdoc/>
    public async Task<Result> ReplayMessageAsync(
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            return Result.Failure(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed."));
        }

        ServiceBusReceiver? dlqReceiver = null;
        ServiceBusSender? sender = null;
        var messagesToAbandon = new List<ServiceBusReceivedMessage>();

        try
        {
            // Create receiver for dead-letter queue
            var receiverOptions = new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.DeadLetter,
                PrefetchCount = 0 // Disable prefetch for more control
            };

            if (!string.IsNullOrEmpty(subscriptionName))
            {
                dlqReceiver = _client.CreateReceiver(entityName, subscriptionName, receiverOptions);
            }
            else
            {
                dlqReceiver = _client.CreateReceiver(entityName, receiverOptions);
            }

            // Receive messages in batches to find the target message more efficiently
            ServiceBusReceivedMessage? targetMessage = null;
            const int maxAttempts = 10;
            const int batchSize = 50;
            
            for (int attempt = 0; attempt < maxAttempts && targetMessage == null; attempt++)
            {
                var messages = await dlqReceiver.ReceiveMessagesAsync(
                    maxMessages: batchSize, 
                    maxWaitTime: TimeSpan.FromSeconds(3),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (messages == null || messages.Count == 0)
                {
                    break; // No more messages available
                }

                foreach (var msg in messages)
                {
                    if (msg.SequenceNumber == sequenceNumber)
                    {
                        targetMessage = msg;
                    }
                    else
                    {
                        messagesToAbandon.Add(msg);
                    }
                }
            }

            if (targetMessage == null)
            {
                return Result.Failure(Error.NotFound(
                    ErrorCodes.Message.NotFound,
                    $"Message with sequence number {sequenceNumber} not found in dead-letter queue."));
            }

            // Create a new message with the same content
            var replayMessage = new ServiceBusMessage(targetMessage.Body)
            {
                ContentType = targetMessage.ContentType,
                CorrelationId = targetMessage.CorrelationId,
                MessageId = Guid.NewGuid().ToString(), // New message ID
                PartitionKey = targetMessage.PartitionKey,
                SessionId = targetMessage.SessionId,
                ReplyTo = targetMessage.ReplyTo,
                ReplyToSessionId = targetMessage.ReplyToSessionId,
                Subject = targetMessage.Subject,
                TimeToLive = targetMessage.TimeToLive,
                To = targetMessage.To
            };

            // Copy application properties, filtering out DLQ-specific ones
            foreach (var prop in targetMessage.ApplicationProperties)
            {
                // Skip DLQ-specific properties that shouldn't be replayed
                if (!prop.Key.Equals("DeadLetterReason", StringComparison.OrdinalIgnoreCase) &&
                    !prop.Key.Equals("DeadLetterErrorDescription", StringComparison.OrdinalIgnoreCase))
                {
                    replayMessage.ApplicationProperties[prop.Key] = prop.Value;
                }
            }

            // Add replay metadata
            replayMessage.ApplicationProperties["Replayed"] = true;
            replayMessage.ApplicationProperties["ReplayedAt"] = DateTime.UtcNow.ToString("O");
            replayMessage.ApplicationProperties["OriginalSequenceNumber"] = sequenceNumber;
            replayMessage.ApplicationProperties["OriginalDeadLetterReason"] = targetMessage.DeadLetterReason ?? "Unknown";

            // Send to main queue/topic
            sender = _client.CreateSender(entityName);
            await sender.SendMessageAsync(replayMessage, cancellationToken).ConfigureAwait(false);

            // Complete (remove) the message from DLQ
            await dlqReceiver.CompleteMessageAsync(targetMessage, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Successfully replayed message {SequenceNumber} from {EntityName} DLQ to main queue",
                sequenceNumber,
                entityName);

            return Result.Success();
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.LogWarning(ex, "Entity {EntityName} not found", entityName);
            return Result.Failure(Error.NotFound(
                ErrorCodes.Queue.NotFound,
                $"The queue or topic '{entityName}' was not found."));
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex, "Service Bus error replaying message from {EntityName}", entityName);
            return Result.Failure(Error.ExternalService(
                ErrorCodes.Message.SendFailed,
                $"Failed to replay message: {ex.Reason}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error replaying message from {EntityName}", entityName);
            return Result.Failure(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while replaying the message."));
        }
        finally
        {
            // Abandon all messages we didn't need so they become available again
            if (dlqReceiver != null)
            {
                foreach (var msg in messagesToAbandon)
                {
                    try
                    {
                        await dlqReceiver.AbandonMessageAsync(msg, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best effort - ignore errors during cleanup
                    }
                }
                await dlqReceiver.DisposeAsync().ConfigureAwait(false);
            }
            if (sender != null)
            {
                await sender.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyDictionary<long, Result>> ReplayMessagesAsync(
        string entityName,
        string? subscriptionName,
        IReadOnlyCollection<long> sequenceNumbers,
        CancellationToken cancellationToken = default)
    {
        var results = new Dictionary<long, Result>();

        if (sequenceNumbers.Count == 0)
            return results;

        if (_disposed || _client.IsClosed)
        {
            var error = Result.Failure(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed."));
            foreach (var seq in sequenceNumbers)
                results[seq] = error;
            return results;
        }

        ServiceBusReceiver? dlqReceiver = null;
        ServiceBusSender? sender = null;
        var messagesToAbandon = new List<ServiceBusReceivedMessage>();

        try
        {
            // Create a SINGLE receiver for the DLQ — all target messages are in the same entity
            var receiverOptions = new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.DeadLetter,
                PrefetchCount = 0
            };

            dlqReceiver = !string.IsNullOrEmpty(subscriptionName)
                ? _client.CreateReceiver(entityName, subscriptionName, receiverOptions)
                : _client.CreateReceiver(entityName, receiverOptions);

            sender = _client.CreateSender(entityName);

            // Build the set of target sequence numbers we're looking for
            var pending = new HashSet<long>(sequenceNumbers);
            var foundMessages = new Dictionary<long, ServiceBusReceivedMessage>();

            // Receive messages in batches until we find all targets or exhaust the DLQ
            const int maxAttempts = 10;
            const int batchSize = 100; // Larger batch for bulk operations

            for (int attempt = 0; attempt < maxAttempts && pending.Count > 0; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var messages = await dlqReceiver.ReceiveMessagesAsync(
                    maxMessages: batchSize,
                    maxWaitTime: TimeSpan.FromSeconds(5),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (messages == null || messages.Count == 0)
                    break; // No more messages in the DLQ

                foreach (var msg in messages)
                {
                    if (pending.Remove(msg.SequenceNumber))
                    {
                        foundMessages[msg.SequenceNumber] = msg;
                    }
                    else
                    {
                        messagesToAbandon.Add(msg);
                    }
                }
            }

            // Mark any not-found messages as failure
            foreach (var seq in pending)
            {
                results[seq] = Result.Failure(Error.NotFound(
                    ErrorCodes.Message.NotFound,
                    $"Message with sequence number {seq} not found in dead-letter queue."));
            }

            // Replay all found messages
            foreach (var (seqNum, targetMessage) in foundMessages)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    // Create a new message with the same content
                    var replayMessage = new ServiceBusMessage(targetMessage.Body)
                    {
                        ContentType = targetMessage.ContentType,
                        CorrelationId = targetMessage.CorrelationId,
                        MessageId = Guid.NewGuid().ToString(),
                        PartitionKey = targetMessage.PartitionKey,
                        SessionId = targetMessage.SessionId,
                        ReplyTo = targetMessage.ReplyTo,
                        ReplyToSessionId = targetMessage.ReplyToSessionId,
                        Subject = targetMessage.Subject,
                        TimeToLive = targetMessage.TimeToLive,
                        To = targetMessage.To
                    };

                    // Copy application properties, filtering out DLQ-specific ones
                    foreach (var prop in targetMessage.ApplicationProperties)
                    {
                        if (!prop.Key.Equals("DeadLetterReason", StringComparison.OrdinalIgnoreCase) &&
                            !prop.Key.Equals("DeadLetterErrorDescription", StringComparison.OrdinalIgnoreCase))
                        {
                            replayMessage.ApplicationProperties[prop.Key] = prop.Value;
                        }
                    }

                    // Add replay metadata
                    replayMessage.ApplicationProperties["Replayed"] = true;
                    replayMessage.ApplicationProperties["ReplayedAt"] = DateTime.UtcNow.ToString("O");
                    replayMessage.ApplicationProperties["OriginalSequenceNumber"] = seqNum;
                    replayMessage.ApplicationProperties["OriginalDeadLetterReason"] = targetMessage.DeadLetterReason ?? "Unknown";

                    // Send to main queue/topic
                    await sender.SendMessageAsync(replayMessage, cancellationToken).ConfigureAwait(false);

                    // Complete (remove) the message from DLQ
                    await dlqReceiver.CompleteMessageAsync(targetMessage, cancellationToken).ConfigureAwait(false);

                    _logger.LogInformation(
                        "Batch-replayed message {SequenceNumber} from {EntityName} DLQ",
                        seqNum, entityName);

                    results[seqNum] = Result.Success();
                }
                catch (ServiceBusException ex)
                {
                    _logger.LogError(ex, "Service Bus error replaying message {SequenceNumber} from {EntityName}", seqNum, entityName);
                    results[seqNum] = Result.Failure(Error.ExternalService(
                        ErrorCodes.Message.SendFailed,
                        $"Failed to replay message {seqNum}: {ex.Reason}"));
                    // Continue with remaining messages — don't abort the batch
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error replaying message {SequenceNumber} from {EntityName}", seqNum, entityName);
                    results[seqNum] = Result.Failure(Error.Internal(
                        ErrorCodes.General.UnexpectedError,
                        $"Failed to replay message {seqNum}: {ex.Message}"));
                }
            }

            return results;
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.LogWarning(ex, "Entity {EntityName} not found for batch replay", entityName);
            var error = Result.Failure(Error.NotFound(
                ErrorCodes.Queue.NotFound,
                $"The queue or topic '{entityName}' was not found."));
            foreach (var seq in sequenceNumbers)
                results.TryAdd(seq, error);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Batch replay failed for {EntityName}", entityName);
            var error = Result.Failure(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                $"Batch replay failed: {ex.Message}"));
            foreach (var seq in sequenceNumbers)
                results.TryAdd(seq, error);
            return results;
        }
        finally
        {
            // Abandon all non-target messages so they become available again
            if (dlqReceiver != null)
            {
                foreach (var msg in messagesToAbandon)
                {
                    try
                    {
                        await dlqReceiver.AbandonMessageAsync(msg, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best effort
                    }
                }
                await dlqReceiver.DisposeAsync().ConfigureAwait(false);
            }
            if (sender != null)
            {
                await sender.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <inheritdoc/>
    public async Task<Result> PurgeMessageAsync(
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        bool fromDeadLetter,
        CancellationToken cancellationToken = default)
    {
        if (_disposed || _client.IsClosed)
        {
            return Result.Failure(Error.Internal(
                ErrorCodes.General.ServiceUnavailable,
                "The Service Bus client has been disposed or closed."));
        }

        ServiceBusReceiver? receiver = null;
        var messagesToAbandon = new List<ServiceBusReceivedMessage>();

        try
        {
            // Create receiver options
            var receiverOptions = new ServiceBusReceiverOptions
            {
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                PrefetchCount = 0 // Disable prefetch for more control
            };

            if (fromDeadLetter)
            {
                receiverOptions.SubQueue = SubQueue.DeadLetter;
            }

            // Create receiver
            if (!string.IsNullOrEmpty(subscriptionName))
            {
                receiver = _client.CreateReceiver(entityName, subscriptionName, receiverOptions);
            }
            else
            {
                receiver = _client.CreateReceiver(entityName, receiverOptions);
            }

            // Receive messages in batches to find the target message more efficiently
            // For active subscriptions with many messages, we need to scan more messages
            ServiceBusReceivedMessage? targetMessage = null;
            const int maxAttempts = 20; // Increased from 10 to handle larger queues
            const int batchSize = 100; // Increased from 50 for faster scanning

            _logger.LogDebug(
                "Starting purge scan for sequence {SequenceNumber} in {EntityName}/{SubscriptionName}",
                sequenceNumber,
                entityName,
                subscriptionName ?? "N/A");

            for (int attempt = 0; attempt < maxAttempts && targetMessage == null; attempt++)
            {
                var messages = await receiver.ReceiveMessagesAsync(
                    maxMessages: batchSize, 
                    maxWaitTime: TimeSpan.FromSeconds(2),
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                if (messages == null || messages.Count == 0)
                {
                    _logger.LogDebug("No more messages available after {Attempt} attempts", attempt + 1);
                    break; // No more messages available
                }

                _logger.LogDebug("Received {Count} messages in batch {Attempt}", messages.Count, attempt + 1);

                foreach (var msg in messages)
                {
                    if (msg.SequenceNumber == sequenceNumber)
                    {
                        targetMessage = msg;
                        _logger.LogDebug("Found target message at sequence {SequenceNumber}", sequenceNumber);
                    }
                    else
                    {
                        messagesToAbandon.Add(msg);
                    }
                }
            }

            if (targetMessage == null)
            {
                _logger.LogWarning(
                    "Message with sequence {SequenceNumber} not found after scanning {MaxAttempts} batches", 
                    sequenceNumber,
                    maxAttempts);
                return Result.Failure(Error.NotFound(
                    ErrorCodes.Message.NotFound,
                    $"Message with sequence number {sequenceNumber} not found after scanning {maxAttempts * batchSize} messages."));
            }

            // Complete (delete) the message
            await receiver.CompleteMessageAsync(targetMessage, cancellationToken).ConfigureAwait(false);

            var queueType = fromDeadLetter ? "dead-letter queue" : (string.IsNullOrEmpty(subscriptionName) ? "queue" : "subscription");
            _logger.LogInformation(
                "Successfully purged message {SequenceNumber} from {EntityName}/{SubscriptionName} {QueueType}",
                sequenceNumber,
                entityName,
                subscriptionName ?? "N/A",
                queueType);

            return Result.Success();
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.MessagingEntityNotFound)
        {
            _logger.LogWarning(ex, "Entity {EntityName} not found", entityName);
            return Result.Failure(Error.NotFound(
                ErrorCodes.Queue.NotFound,
                $"The queue or topic '{entityName}' was not found."));
        }
        catch (ServiceBusException ex)
        {
            _logger.LogError(ex, "Service Bus error purging message from {EntityName}", entityName);
            return Result.Failure(Error.ExternalService(
                ErrorCodes.Message.ReceiveFailed,
                $"Failed to purge message: {ex.Reason}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error purging message from {EntityName}", entityName);
            return Result.Failure(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                "An unexpected error occurred while purging the message."));
        }
        finally
        {
            // Abandon all messages we didn't need so they become available again
            if (receiver != null)
            {
                foreach (var msg in messagesToAbandon)
                {
                    try
                    {
                        await receiver.AbandonMessageAsync(msg, cancellationToken: CancellationToken.None).ConfigureAwait(false);
                    }
                    catch
                    {
                        // Best effort - ignore errors during cleanup
                    }
                }
                await receiver.DisposeAsync().ConfigureAwait(false);
            }
        }
    }
}
