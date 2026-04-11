using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for managing Service Bus queues.
/// Provides endpoints for listing queues and their metadata.
/// </summary>
[Route(ApiRoutes.Queues.Base)]
[Tags("Queues")]
public sealed class QueuesController : ApiControllerBase
{
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IServiceBusClientCache _clientCache;
    private readonly IConnectionStringProtector _connectionStringProtector;
    private readonly IMessageSender _messageSender;
    private readonly IMessageReceiver _messageReceiver;
    private readonly ILogger<QueuesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueuesController"/> class.
    /// </summary>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="connectionStringProtector">The connection string protector.</param>
    /// <param name="messageSender">The message sender service.</param>
    /// <param name="messageReceiver">The message receiver service.</param>
    /// <param name="logger">The logger.</param>
    public QueuesController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        IMessageSender messageSender,
        IMessageReceiver messageReceiver,
        ILogger<QueuesController> logger)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _messageSender = messageSender ?? throw new ArgumentNullException(nameof(messageSender));
        _messageReceiver = messageReceiver ?? throw new ArgumentNullException(nameof(messageReceiver));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets all queues for a namespace.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of queue information.</returns>
    /// <response code="200">Queues retrieved successfully.</response>
    /// <response code="404">Namespace not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.QueuesRead)]
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<QueueRuntimePropertiesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<QueueRuntimePropertiesDto>>> GetAll(
        [FromRoute] Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting all queues for namespace {NamespaceId}", namespaceId);

        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<QueueRuntimePropertiesDto>>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;
        if (!string.Equals(ns.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return NotFound();
        }

        if (ns.ConnectionString is null)
        {
            return BadRequest("Namespace does not have a connection string configured.");
        }

        var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<QueueRuntimePropertiesDto>>(unprotectResult.Error);
        }

        var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
        var queuesResult = await wrapper.GetQueuesAsync(cancellationToken);
        if (queuesResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<QueueRuntimePropertiesDto>>(queuesResult.Error);
        }

        _logger.LogInformation(
            "Retrieved {QueueCount} queues for namespace {NamespaceId}",
            queuesResult.Value.Count,
            namespaceId);

        return Ok(queuesResult.Value);
    }

    /// <summary>
    /// Gets information about a specific queue.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="queueName">The queue name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The queue information.</returns>
    /// <response code="200">Queue retrieved successfully.</response>
    /// <response code="404">Namespace or queue not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.QueuesRead)]
    [HttpGet("{queueName}")]
    [ProducesResponseType(typeof(QueueRuntimePropertiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<QueueRuntimePropertiesDto>> GetByName(
        [FromRoute] Guid namespaceId,
        [FromRoute] string queueName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Getting queue {QueueName} for namespace {NamespaceId}",
            LogRedactor.SanitiseForLog(queueName),
            namespaceId);

        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<QueueRuntimePropertiesDto>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;
        if (!string.Equals(ns.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return NotFound();
        }

        if (ns.ConnectionString is null)
        {
            return BadRequest("Namespace does not have a connection string configured.");
        }

        var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
        {
            return ToActionResult<QueueRuntimePropertiesDto>(unprotectResult.Error);
        }

        var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
        var queueResult = await wrapper.GetQueueAsync(queueName, cancellationToken);
        if (queueResult.IsFailure)
        {
            return ToActionResult<QueueRuntimePropertiesDto>(queueResult.Error);
        }

        return Ok(queueResult.Value);
    }

    /// <summary>
    /// Sends a message to a queue.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="queueName">The queue name.</param>
    /// <param name="request">The send message request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Accepted if successful.</returns>
    /// <response code="202">Message accepted for delivery.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="403">Insufficient permissions.</response>
    /// <response code="404">Namespace or queue not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.MessagesSend)]
    [HttpPost("{queueName}/messages")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SendMessage(
        [FromRoute] Guid namespaceId,
        [FromRoute] string queueName,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Sending message to queue {QueueName} in namespace {NamespaceId}",
            LogRedactor.SanitiseForLog(queueName),
            namespaceId);

        // Verify namespace exists
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

        // Check if namespace has Send permission (required to send messages)
        if (!ns.HasSendPermission)
        {
            return StatusCode(
                StatusCodes.Status403Forbidden,
                new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Insufficient Permissions",
                    Detail = "Send operations require 'Send' permission. " +
                           "Update your connection string to use a policy with Manage, Send, and Listen permissions."
                });
        }

        // Production safety guard — block direct message sends to production namespaces
        if (ns.Environment == EnvironmentType.Prod)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Production Restriction",
                detail: "Sending messages directly to production namespaces is not permitted via ServiceHub. " +
                       "Use your CI/CD pipeline or approved tooling for production message operations.");
        }

        // Create a request with the queue name and namespace ID
        var sendRequest = request with 
        { 
            EntityName = queueName,
            NamespaceId = namespaceId
        };

        var result = await _messageSender.SendAsync(sendRequest, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        _logger.LogInformation("Message sent to queue {QueueName}", LogRedactor.SanitiseForLog(queueName));
        return Accepted();
    }

    /// <summary>
    /// Peeks messages from a queue (active or dead-letter).
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="queueName">The queue name.</param>
    /// <param name="queueType">Queue type: active or deadletter.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take (clamped to max 1000).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A paginated list of messages.</returns>
    /// <response code="200">Messages retrieved successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="404">Namespace or queue not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.MessagesPeek)]
    [HttpGet("{queueName}/messages")]
    [ProducesResponseType(typeof(PaginatedResponse<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PaginatedResponse<MessageResponse>>> GetMessages(
        [FromRoute] Guid namespaceId,
        [FromRoute] string queueName,
        [FromQuery] string queueType = "active",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Peeking messages from queue {QueueName} in namespace {NamespaceId}",
            LogRedactor.SanitiseForLog(queueName),
            namespaceId);

        var fromDeadLetter = string.Equals(queueType, "deadletter", StringComparison.OrdinalIgnoreCase);
        var pageSize = Math.Clamp(take, GetMessagesRequest.MinAllowedMessages, GetMessagesRequest.MaxAllowedMessages);
        var request = new GetMessagesRequest(
            NamespaceId: namespaceId,
            EntityName: queueName,
            SubscriptionName: null,
            FromDeadLetter: fromDeadLetter,
            MaxMessages: pageSize,
            FromSequenceNumber: null);

        var result = fromDeadLetter
            ? await _messageReceiver.PeekDeadLetterMessagesAsync(request, cancellationToken)
            : await _messageReceiver.PeekMessagesAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<PaginatedResponse<MessageResponse>>(result.Error);
        }

        // Get the actual total count from queue runtime properties
        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        int totalCount = result.Value.Count; // Default to peeked count
        
        if (namespaceResult.IsSuccess && namespaceResult.Value.ConnectionString is not null)
        {
            try 
            {
                var unprotectResult = _connectionStringProtector.Unprotect(namespaceResult.Value.ConnectionString);
                if (unprotectResult.IsSuccess)
                {
                    var wrapper = _clientCache.GetOrCreate(namespaceResult.Value.Id, unprotectResult.Value);
                    var queuesResult = await wrapper.GetQueuesAsync(cancellationToken);
                    if (queuesResult.IsSuccess)
                    {
                        var queueInfo = queuesResult.Value.FirstOrDefault(q => string.Equals(q.Name, queueName, StringComparison.OrdinalIgnoreCase));
                        if (queueInfo is not null)
                        {
                            totalCount = (int)(fromDeadLetter ? queueInfo.DeadLetterMessageCount : queueInfo.ActiveMessageCount);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get queue runtime properties for accurate count");
            }
        }

        var page = pageSize > 0 ? (skip / pageSize) + 1 : 1;
        var items = result.Value
            .Skip(Math.Max(skip, 0))
            .Take(pageSize)
            .Select(MapToResponse)
            .ToList();

        var response = new PaginatedResponse<MessageResponse>(
            Items: items,
            TotalCount: totalCount,
            Page: page,
            PageSize: pageSize,
            HasNextPage: skip + pageSize < totalCount,
            HasPreviousPage: skip > 0);

        return Ok(response);
    }

    /// <summary>
    /// Moves messages from a queue to its dead-letter queue for testing purposes.
    /// </summary>
    /// <remarks>
    /// This endpoint is useful for testing DLQ handling without waiting for real failures.
    /// It moves messages from the active queue to the dead-letter queue.
    /// </remarks>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="queueName">The queue name.</param>
    /// <param name="messageCount">Number of messages to dead-letter (default 1, max 10).</param>
    /// <param name="reason">The dead-letter reason.</param>
    /// <param name="errorDescription">Optional error description.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Number of messages dead-lettered.</returns>
    /// <response code="200">Messages dead-lettered successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="404">Namespace or queue not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.MessagesSend)]
    [HttpPost("{queueName}/deadletter")]
    [ProducesResponseType(typeof(DeadLetterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<DeadLetterResponse>> DeadLetterMessages(
        [FromRoute] Guid namespaceId,
        [FromRoute] string queueName,
        [FromQuery] int messageCount = 1,
        [FromQuery] string reason = "ManualDeadLetter",
        [FromQuery] string? errorDescription = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Dead-lettering {Count} messages from queue {QueueName} in namespace {NamespaceId} with reason: {Reason}",
            messageCount,
            LogRedactor.SanitiseForLog(queueName),
            namespaceId,
            LogRedactor.SanitiseForLog(reason));

        // Get namespace to check permissions
        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<DeadLetterResponse>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;
        if (!string.Equals(ns.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return NotFound();
        }
        
        // Check if namespace has Send permission (required to dead-letter messages)
        if (!ns.HasSendPermission)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Insufficient Permissions",
                detail: "The configured connection string lacks 'Send' permission. " +
                       "Dead-letter operations require 'Send' permission to move messages to the dead-letter queue. " +
                       "Please create or use a Shared Access Policy with 'Manage', 'Send', and 'Listen' permissions.",
                type: "https://docs.microsoft.com/azure/service-bus-messaging/service-bus-sas");
        }

        // Production safety guard — block dead-lettering in production namespaces
        if (ns.Environment == EnvironmentType.Prod)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Production Restriction",
                detail: "Dead-lettering messages in production namespaces is not permitted via ServiceHub. " +
                       "Use your CI/CD pipeline or approved tooling for production message operations.");
        }

        var request = new DeadLetterRequest(
            NamespaceId: namespaceId,
            EntityName: queueName,
            SubscriptionName: null,
            MessageCount: messageCount,
            Reason: reason,
            ErrorDescription: errorDescription);

        var result = await _messageReceiver.DeadLetterMessagesAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<DeadLetterResponse>(result.Error);
        }

        _logger.LogInformation(
            "Successfully dead-lettered {Count} messages from queue {QueueName}",
            result.Value,
            LogRedactor.SanitiseForLog(queueName));

        return Ok(new DeadLetterResponse(result.Value, reason));
    }

    /// <summary>
    /// Lists scheduled messages in a queue (messages with a future enqueue time).
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="queueName">The queue name.</param>
    /// <param name="skip">Number of messages to skip for pagination.</param>
    /// <param name="take">Maximum number of messages to return (default 100, max 1000).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A paginated list of scheduled messages.</returns>
    /// <response code="200">Scheduled messages retrieved successfully.</response>
    /// <response code="404">Namespace or queue not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.MessagesPeek)]
    [HttpGet("{queueName}/scheduled")]
    [ProducesResponseType(typeof(PaginatedResponse<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PaginatedResponse<MessageResponse>>> GetScheduledMessages(
        [FromRoute] Guid namespaceId,
        [FromRoute] string queueName,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Listing scheduled messages in queue {QueueName} for namespace {NamespaceId}",
            LogRedactor.SanitiseForLog(queueName),
            namespaceId);

        var pageSize = Math.Clamp(take, 1, 1000);

        // Use the dedicated scheduled-message retrieval so that we scan beyond the active-message
        // window.  A plain PeekMessagesAsync is capped at MaxAllowedMessages (100) and misses
        // scheduled messages that have higher sequence numbers than the active backlog.
        var result = await _messageReceiver.GetScheduledMessagesAsync(
            namespaceId,
            queueName,
            subscriptionName: null,
            maxMessages: 1000,
            cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<PaginatedResponse<MessageResponse>>(result.Error);
        }

        var scheduledItems = result.Value;

        // The Azure Service Bus data-plane PeekMessages API cannot access the scheduled-message
        // broker store; only the active-message store is reachable via peek.  Use the queue
        // runtime properties for the accurate scheduled-message count so that the UI always
        // shows the correct total even though individual message content cannot be retrieved.
        var totalScheduled = scheduledItems.Count;
        if (totalScheduled == 0)
        {
            try
            {
                var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
                if (namespaceResult.IsSuccess && namespaceResult.Value.ConnectionString is not null)
                {
                    var unprotectResult = _connectionStringProtector.Unprotect(namespaceResult.Value.ConnectionString);
                    if (unprotectResult.IsSuccess)
                    {
                        var wrapper = _clientCache.GetOrCreate(namespaceResult.Value.Id, unprotectResult.Value);
                        var queuesResult = await wrapper.GetQueuesAsync(cancellationToken);
                        if (queuesResult.IsSuccess)
                        {
                            var queueInfo = queuesResult.Value.FirstOrDefault(q =>
                                string.Equals(q.Name, queueName, StringComparison.OrdinalIgnoreCase));
                            if (queueInfo is not null)
                            {
                                totalScheduled = (int)queueInfo.ScheduledMessageCount;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get queue runtime properties for scheduled message count");
            }
        }

        var items = scheduledItems
            .Skip(Math.Max(skip, 0))
            .Take(pageSize)
            .Select(MapToResponse)
            .ToList();

        var page = pageSize > 0 ? (skip / pageSize) + 1 : 1;
        var response = new PaginatedResponse<MessageResponse>(
            Items: items,
            TotalCount: totalScheduled,
            Page: page,
            PageSize: pageSize,
            HasNextPage: skip + pageSize < totalScheduled,
            HasPreviousPage: skip > 0);

        _logger.LogInformation(
            "Found {Count} scheduled messages in queue {QueueName}",
            totalScheduled,
            LogRedactor.SanitiseForLog(queueName));

        return Ok(response);
    }

    /// <summary>
    /// Cancels a scheduled message by its sequence number.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="queueName">The queue name.</param>
    /// <param name="sequenceNumber">The sequence number of the scheduled message to cancel.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">Scheduled message cancelled successfully.</response>
    /// <response code="404">Namespace, queue, or scheduled message not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.MessagesSend)]
    [HttpDelete("{queueName}/scheduled/{sequenceNumber:long}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> CancelScheduledMessage(
        [FromRoute] Guid namespaceId,
        [FromRoute] string queueName,
        [FromRoute] long sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Cancelling scheduled message {SequenceNumber} in queue {QueueName} for namespace {NamespaceId}",
            sequenceNumber,
            LogRedactor.SanitiseForLog(queueName),
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

        // Cancelling a scheduled message requires Send permission
        if (!ns.HasSendPermission)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Insufficient Permissions",
                detail: "Cancelling scheduled messages requires 'Send' permission.");
        }

        // Production safety guard
        if (ns.Environment == EnvironmentType.Prod)
        {
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Production Restriction",
                detail: "Cancelling scheduled messages in production namespaces is not permitted via ServiceHub.");
        }

        if (ns.ConnectionString is null)
        {
            return BadRequest("Namespace does not have a connection string configured.");
        }

        var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
        {
            return ToActionResult(Shared.Results.Result.Failure(unprotectResult.Error));
        }

        var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
        var result = await wrapper.CancelScheduledMessageAsync(queueName, sequenceNumber, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        _logger.LogInformation(
            "Cancelled scheduled message {SequenceNumber} in queue {QueueName}",
            sequenceNumber,
            LogRedactor.SanitiseForLog(queueName));

        return NoContent();
    }

    private static MessageResponse MapToResponse(ServiceHub.Core.Entities.Message message)
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
