using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Security;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

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
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<MessagesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagesController"/> class.
    /// </summary>
    /// <param name="messageSender">The message sender service.</param>
    /// <param name="messageReceiver">The message receiver service.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="auditLogger">The security audit logger.</param>
    public MessagesController(
        IMessageSender messageSender,
        IMessageReceiver messageReceiver,
        INamespaceRepository namespaceRepository,
        ILogger<MessagesController> logger,
        IAuditLogger? auditLogger = null)
    {
        _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
        _messageReceiver = messageReceiver ?? throw new ArgumentNullException(nameof(messageReceiver));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _auditLogger = auditLogger ?? NoOpAuditLogger.Instance;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
    [RequireScope(ApiKeyScopes.MessagesPeek)]
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
            LogRedactor.SanitiseForLog(queueName),
            namespaceId);

        // TENANT ISOLATION: Verify the namespace belongs to the current authenticated user.
        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(namespaceResult.Error);
        }

        if (!string.Equals(namespaceResult.Value.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(Error.NotFound(
                ErrorCodes.Namespace.NotFound,
                $"Namespace with ID '{namespaceId}' was not found."));
        }

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
    [RequireScope(ApiKeyScopes.MessagesPeek)]
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
            LogRedactor.SanitiseForLog(subscriptionName),
            LogRedactor.SanitiseForLog(topicName),
            namespaceId);

        // TENANT ISOLATION: Verify the namespace belongs to the current authenticated user.
        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(namespaceResult.Error);
        }

        if (!string.Equals(namespaceResult.Value.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(Error.NotFound(
                ErrorCodes.Namespace.NotFound,
                $"Namespace with ID '{namespaceId}' was not found."));
        }

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
    [RequireScope(ApiKeyScopes.MessagesPeek)]
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
            LogRedactor.SanitiseForLog(queueName),
            namespaceId);

        // TENANT ISOLATION: Verify the namespace belongs to the current authenticated user.
        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(namespaceResult.Error);
        }

        if (!string.Equals(namespaceResult.Value.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(Error.NotFound(
                ErrorCodes.Namespace.NotFound,
                $"Namespace with ID '{namespaceId}' was not found."));
        }

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
    [RequireScope(ApiKeyScopes.MessagesPeek)]
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
            LogRedactor.SanitiseForLog(subscriptionName),
            LogRedactor.SanitiseForLog(topicName),
            namespaceId);

        // TENANT ISOLATION: Verify the namespace belongs to the current authenticated user.
        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(namespaceResult.Error);
        }

        if (!string.Equals(namespaceResult.Value.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(Error.NotFound(
                ErrorCodes.Namespace.NotFound,
                $"Namespace with ID '{namespaceId}' was not found."));
        }

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

    /// <summary>
    /// Replays a message from the dead-letter queue back to the main queue.
    /// </summary>
    /// <remarks>
    /// This endpoint moves a message from DLQ back to the main queue for reprocessing.
    /// ServiceHub is primarily read-only, but this operation is essential for DLQ recovery.
    /// </remarks>
    [RequireScope(ApiKeyScopes.MessagesSend)]
    [HttpPost("replay")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ReplayMessage(
        [FromQuery] Guid namespaceId,
        [FromQuery] long sequenceNumber,
        [FromQuery] string entityName,
        [FromQuery] string? subscriptionName = null,
        CancellationToken cancellationToken = default)
    {
        if (!IntentHeaders.HasExplicitIntent(HttpContext, IntentHeaders.IntentReplayMessage))
        {
            _auditLogger.LogCriticalAction(
                HttpContext,
                OwnerId,
                action: IntentHeaders.IntentReplayMessage,
                outcome: "Denied",
                namespaceId: namespaceId,
                resourceName: entityName,
                sequenceNumber: sequenceNumber,
                detail: "Missing explicit intent headers");

            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Explicit Intent Required",
                detail: IntentHeaders.BuildIntentRequiredDetail("message replay"));
        }

        _logger.LogInformation(
            "Replaying message {SequenceNumber} from {EntityName} in namespace {NamespaceId}",
            sequenceNumber,
            LogRedactor.SanitiseForLog(entityName),
            namespaceId);

        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult(Shared.Results.Result.Failure(namespaceResult.Error));
        }

        var ns = namespaceResult.Value;
        if (!string.Equals(ns.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return NotFound();
        }
        
        // Check if namespace has Send permission
        if (!ns.HasSendPermission)
        {
            _auditLogger.LogCriticalAction(
                HttpContext,
                OwnerId,
                action: IntentHeaders.IntentReplayMessage,
                outcome: "Denied",
                namespaceId: namespaceId,
                environment: ns.Environment,
                resourceName: entityName,
                sequenceNumber: sequenceNumber,
                detail: "Namespace lacks Send permission");

            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Insufficient Permissions",
                detail: "The configured connection string lacks 'Send' permission. " +
                       "Replay operations require 'Send' permission to move messages from DLQ back to the main queue. " +
                       "Please create or use a Shared Access Policy with 'Manage', 'Send', and 'Listen' permissions for full functionality.",
                type: "https://docs.microsoft.com/azure/service-bus-messaging/service-bus-sas");
        }

        // Safety-by-default guard: destructive replay is blocked in production.
        if (ns.Environment == EnvironmentType.Prod)
        {
            _auditLogger.LogCriticalAction(
                HttpContext,
                OwnerId,
                action: IntentHeaders.IntentReplayMessage,
                outcome: "Denied",
                namespaceId: namespaceId,
                environment: ns.Environment,
                resourceName: entityName,
                sequenceNumber: sequenceNumber,
                detail: "Replay blocked in production environment");

            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Production Restriction",
                detail: "Replay is blocked for production namespaces. Validate in DEV and UAT first.");
        }

        var result = await _messageReceiver.ReplayMessageAsync(
            namespaceId,
            entityName,
            subscriptionName,
            sequenceNumber,
            cancellationToken);

        if (result.IsFailure)
        {
            _auditLogger.LogCriticalAction(
                HttpContext,
                OwnerId,
                action: IntentHeaders.IntentReplayMessage,
                outcome: "Failed",
                namespaceId: namespaceId,
                environment: ns.Environment,
                resourceName: entityName,
                sequenceNumber: sequenceNumber,
                detail: result.Error.Message);
            return ToActionResult(result);
        }

        _auditLogger.LogCriticalAction(
            HttpContext,
            OwnerId,
            action: IntentHeaders.IntentReplayMessage,
            outcome: "Succeeded",
            namespaceId: namespaceId,
            environment: ns.Environment,
            resourceName: entityName,
            sequenceNumber: sequenceNumber,
            detail: "Replay completed");

        _logger.LogInformation("Message {SequenceNumber} replayed successfully", sequenceNumber);
        return Accepted();
    }

    /* PURGE ENDPOINT DISABLED - Azure Service Bus Limitation
     * 
     * The Azure Service Bus SDK does not support direct access to messages by sequence number
     * for active queues/subscriptions. The only way to delete a specific message is to:
     * 1. Receive messages in batches (which locks them)
     * 2. Scan through each batch looking for the target sequence number
     * 3. Complete (delete) the target and abandon all others
     * 
     * This approach is fundamentally flawed because:
     * - It's extremely slow for large queues (O(n) complexity)
     * - It locks messages during scanning, affecting other consumers
     * - It times out for queues with many messages (>100 messages)
     * - Race conditions with concurrent consumers
     * 
     * This feature can be re-enabled if Microsoft adds support for targeted message deletion.
     * 
    [RequireScope(ApiKeyScopes.MessagesSend)]
    [HttpDelete("purge")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> PurgeMessage(
        [FromQuery] Guid namespaceId,
        [FromQuery] long sequenceNumber,
        [FromQuery] string entityName,
        [FromQuery] string? subscriptionName = null,
        [FromQuery] bool fromDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult(Shared.Results.Result.Failure(namespaceResult.Error));
        }

        var result = await _messageReceiver.PurgeMessageAsync(
            namespaceId,
            entityName,
            subscriptionName,
            sequenceNumber,
            fromDeadLetter,
            cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        return NoContent();
    }
    */
}
