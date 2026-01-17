using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for managing Service Bus messages.
/// Provides endpoints for sending, peeking, and managing messages in queues/topics.
/// </summary>
[Route(ApiRoutes.Messages.Base)]
[Tags("Messages")]
public sealed class MessagesController : ApiControllerBase
{
    private readonly IMessageSender _messageSender;
    private readonly IMessageReceiver _messageReceiver;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<MessagesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagesController"/> class.
    /// </summary>
    /// <param name="messageSender">The message sender service.</param>
    /// <param name="messageReceiver">The message receiver service.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="logger">The logger.</param>
    public MessagesController(
        IMessageSender messageSender,
        IMessageReceiver messageReceiver,
        INamespaceRepository namespaceRepository,
        ILogger<MessagesController> logger)
    {
        _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
        _messageReceiver = messageReceiver ?? throw new ArgumentNullException(nameof(messageReceiver));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Sends a message to a queue.
    /// </summary>
    /// <param name="queueName">The queue name.</param>
    /// <param name="request">The send message request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Accepted if successful.</returns>
    /// <response code="202">Message accepted for delivery.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="404">Namespace or queue not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [HttpPost("queue/{queueName}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SendToQueue(
        [FromRoute] string queueName,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Sending message to queue {QueueName} in namespace {NamespaceId}",
            queueName,
            request.NamespaceId);

        // Verify namespace exists
        var namespaceResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult(Shared.Results.Result.Failure(namespaceResult.Error));
        }

        // Create a request with the queue name as entity name
        var sendRequest = request with { EntityName = queueName };

        var result = await _messageSender.SendAsync(sendRequest, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        _logger.LogInformation("Message sent to queue {QueueName}", queueName);
        return Accepted();
    }

    /// <summary>
    /// Sends a message to a topic.
    /// </summary>
    /// <param name="topicName">The topic name.</param>
    /// <param name="request">The send message request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Accepted if successful.</returns>
    /// <response code="202">Message accepted for delivery.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="404">Namespace or topic not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [HttpPost("topic/{topicName}")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SendToTopic(
        [FromRoute] string topicName,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Sending message to topic {TopicName} in namespace {NamespaceId}",
            topicName,
            request.NamespaceId);

        // Verify namespace exists
        var namespaceResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult(Shared.Results.Result.Failure(namespaceResult.Error));
        }

        // Create a request with the topic name as entity name
        var sendRequest = request with { EntityName = topicName };

        var result = await _messageSender.SendAsync(sendRequest, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        _logger.LogInformation("Message sent to topic {TopicName}", topicName);
        return Accepted();
    }

    /// <summary>
    /// Peeks messages from a queue without removing them.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="queueName">The queue name.</param>
    /// <param name="maxMessages">Maximum number of messages to retrieve (1-100).</param>
    /// <param name="fromSequenceNumber">Optional sequence number to start from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of peeked messages.</returns>
    /// <response code="200">Messages retrieved successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="404">Namespace or queue not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [HttpGet("queue/{queueName}")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<MessageResponse>>> PeekQueueMessages(
        [FromQuery] Guid namespaceId,
        [FromRoute] string queueName,
        [FromQuery] int maxMessages = 10,
        [FromQuery] long? fromSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Peeking {MaxMessages} messages from queue {QueueName} in namespace {NamespaceId}",
            maxMessages,
            queueName,
            namespaceId);

        var request = new GetMessagesRequest(
            NamespaceId: namespaceId,
            EntityName: queueName,
            SubscriptionName: null,
            FromDeadLetter: false,
            MaxMessages: Math.Clamp(maxMessages, GetMessagesRequest.MinAllowedMessages, GetMessagesRequest.MaxAllowedMessages),
            FromSequenceNumber: fromSequenceNumber);

        var result = await _messageReceiver.PeekMessagesAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(result.Error);
        }

        var responses = result.Value.Select(MapToResponse).ToList();
        return Ok(responses);
    }

    /// <summary>
    /// Peeks messages from a subscription without removing them.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="topicName">The topic name.</param>
    /// <param name="subscriptionName">The subscription name.</param>
    /// <param name="maxMessages">Maximum number of messages to retrieve (1-100).</param>
    /// <param name="fromSequenceNumber">Optional sequence number to start from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of peeked messages.</returns>
    /// <response code="200">Messages retrieved successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="404">Namespace, topic, or subscription not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [HttpGet("topic/{topicName}/subscription/{subscriptionName}")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<MessageResponse>>> PeekSubscriptionMessages(
        [FromQuery] Guid namespaceId,
        [FromRoute] string topicName,
        [FromRoute] string subscriptionName,
        [FromQuery] int maxMessages = 10,
        [FromQuery] long? fromSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Peeking {MaxMessages} messages from subscription {SubscriptionName} on topic {TopicName} in namespace {NamespaceId}",
            maxMessages,
            subscriptionName,
            topicName,
            namespaceId);

        var request = new GetMessagesRequest(
            NamespaceId: namespaceId,
            EntityName: topicName,
            SubscriptionName: subscriptionName,
            FromDeadLetter: false,
            MaxMessages: Math.Clamp(maxMessages, GetMessagesRequest.MinAllowedMessages, GetMessagesRequest.MaxAllowedMessages),
            FromSequenceNumber: fromSequenceNumber);

        var result = await _messageReceiver.PeekMessagesAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(result.Error);
        }

        var responses = result.Value.Select(MapToResponse).ToList();
        return Ok(responses);
    }

    /// <summary>
    /// Peeks dead letter messages from a queue.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="queueName">The queue name.</param>
    /// <param name="maxMessages">Maximum number of messages to retrieve (1-100).</param>
    /// <param name="fromSequenceNumber">Optional sequence number to start from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of dead letter messages.</returns>
    /// <response code="200">Dead letter messages retrieved successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="404">Namespace or queue not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [HttpGet("queue/{queueName}/deadletter")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<MessageResponse>>> PeekQueueDeadLetterMessages(
        [FromQuery] Guid namespaceId,
        [FromRoute] string queueName,
        [FromQuery] int maxMessages = 10,
        [FromQuery] long? fromSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Peeking {MaxMessages} dead letter messages from queue {QueueName} in namespace {NamespaceId}",
            maxMessages,
            queueName,
            namespaceId);

        var request = new GetMessagesRequest(
            NamespaceId: namespaceId,
            EntityName: queueName,
            SubscriptionName: null,
            FromDeadLetter: true,
            MaxMessages: Math.Clamp(maxMessages, GetMessagesRequest.MinAllowedMessages, GetMessagesRequest.MaxAllowedMessages),
            FromSequenceNumber: fromSequenceNumber);

        var result = await _messageReceiver.PeekDeadLetterMessagesAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(result.Error);
        }

        var responses = result.Value.Select(MapToResponse).ToList();
        return Ok(responses);
    }

    /// <summary>
    /// Peeks dead letter messages from a subscription.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="topicName">The topic name.</param>
    /// <param name="subscriptionName">The subscription name.</param>
    /// <param name="maxMessages">Maximum number of messages to retrieve (1-100).</param>
    /// <param name="fromSequenceNumber">Optional sequence number to start from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of dead letter messages.</returns>
    /// <response code="200">Dead letter messages retrieved successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="404">Namespace, topic, or subscription not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [HttpGet("topic/{topicName}/subscription/{subscriptionName}/deadletter")]
    [ProducesResponseType(typeof(IReadOnlyList<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<MessageResponse>>> PeekSubscriptionDeadLetterMessages(
        [FromQuery] Guid namespaceId,
        [FromRoute] string topicName,
        [FromRoute] string subscriptionName,
        [FromQuery] int maxMessages = 10,
        [FromQuery] long? fromSequenceNumber = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Peeking {MaxMessages} dead letter messages from subscription {SubscriptionName} on topic {TopicName} in namespace {NamespaceId}",
            maxMessages,
            subscriptionName,
            topicName,
            namespaceId);

        var request = new GetMessagesRequest(
            NamespaceId: namespaceId,
            EntityName: topicName,
            SubscriptionName: subscriptionName,
            FromDeadLetter: true,
            MaxMessages: Math.Clamp(maxMessages, GetMessagesRequest.MinAllowedMessages, GetMessagesRequest.MaxAllowedMessages),
            FromSequenceNumber: fromSequenceNumber);

        var result = await _messageReceiver.PeekDeadLetterMessagesAsync(request, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(result.Error);
        }

        var responses = result.Value.Select(MapToResponse).ToList();
        return Ok(responses);
    }

    /// <summary>
    /// Maps a Message entity to a MessageResponse DTO.
    /// </summary>
    /// <param name="message">The message entity.</param>
    /// <returns>The message response.</returns>
    private static MessageResponse MapToResponse(Message message)
    {
        return new MessageResponse(
            MessageId: message.MessageId,
            SequenceNumber: message.SequenceNumber,
            Body: message.Body,
            ContentType: message.ContentType,
            CorrelationId: message.CorrelationId,
            SessionId: message.SessionId,
            PartitionKey: message.PartitionKey,
            Subject: message.Subject,
            ReplyTo: message.ReplyTo,
            ReplyToSessionId: message.ReplyToSessionId,
            To: message.To,
            TimeToLive: message.TimeToLive,
            ScheduledEnqueueTime: message.ScheduledEnqueueTime,
            EnqueuedTime: message.EnqueuedTime,
            ExpiresAt: message.ExpiresAt,
            LockedUntil: message.LockedUntil,
            DeliveryCount: message.DeliveryCount,
            State: message.State,
            DeadLetterSource: message.DeadLetterSource,
            DeadLetterReason: message.DeadLetterReason,
            DeadLetterErrorDescription: message.DeadLetterErrorDescription,
            ApplicationProperties: message.ApplicationProperties,
            SizeInBytes: message.SizeInBytes,
            EntityName: message.EntityName,
            SubscriptionName: message.SubscriptionName,
            IsFromDeadLetter: message.IsFromDeadLetter);
    }
}
