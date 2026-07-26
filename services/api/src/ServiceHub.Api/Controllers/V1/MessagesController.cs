using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Filters;
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
[RequireNamespaceOwnership]
public sealed class MessagesController : ApiControllerBase
{
    private static readonly TimeSpan LiveTailPollInterval = TimeSpan.FromSeconds(3);

    // Auto-closes an idle-forgotten browser tab's stream rather than polling a provider
    // forever; the frontend shows a "reconnect" affordance when this fires.
    private static readonly TimeSpan LiveTailMaxSessionDuration = TimeSpan.FromMinutes(30);

    private static readonly JsonSerializerOptions LiveTailSerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly IMessageOperationsService _messageOperationsService;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ICloudProviderRouter _providerRouter;
    private readonly ILiveTailSessionFactory _liveTailSessionFactory;
    private readonly ILiveTailConnectionLimiter _liveTailConnectionLimiter;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<MessagesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessagesController"/> class.
    /// </summary>
    /// <param name="messageOperationsService">The provider-aware message operations service.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="providerRouter">Resolves a namespace's provider to check Live Tail eligibility.</param>
    /// <param name="liveTailSessionFactory">Creates Live Tail polling sessions.</param>
    /// <param name="liveTailConnectionLimiter">Caps concurrent Live Tail connections.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="auditLogger">The security audit logger.</param>
    public MessagesController(
        IMessageOperationsService messageOperationsService,
        INamespaceRepository namespaceRepository,
        ICloudProviderRouter providerRouter,
        ILiveTailSessionFactory liveTailSessionFactory,
        ILiveTailConnectionLimiter liveTailConnectionLimiter,
        ILogger<MessagesController> logger,
        IAuditLogger? auditLogger = null)
    {
        _messageOperationsService = messageOperationsService ?? throw new ArgumentNullException(nameof(messageOperationsService));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _providerRouter = providerRouter ?? throw new ArgumentNullException(nameof(providerRouter));
        _liveTailSessionFactory = liveTailSessionFactory ?? throw new ArgumentNullException(nameof(liveTailSessionFactory));
        _liveTailConnectionLimiter = liveTailConnectionLimiter ?? throw new ArgumentNullException(nameof(liveTailConnectionLimiter));
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
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(namespaceResult.Error);
        }

        var request = new GetMessagesRequest(
            NamespaceId: namespaceId,
            EntityName: queueName,
            SubscriptionName: null,
            FromDeadLetter: false,
            MaxMessages: Math.Clamp(maxMessages, GetMessagesRequest.MinAllowedMessages, GetMessagesRequest.MaxAllowedMessages),
            FromSequenceNumber: fromSequenceNumber);

        var result = await _messageOperationsService.PeekMessagesAsync(request, cancellationToken);
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
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(namespaceResult.Error);
        }

        var request = new GetMessagesRequest(
            NamespaceId: namespaceId,
            EntityName: topicName,
            SubscriptionName: subscriptionName,
            FromDeadLetter: false,
            MaxMessages: Math.Clamp(maxMessages, GetMessagesRequest.MinAllowedMessages, GetMessagesRequest.MaxAllowedMessages),
            FromSequenceNumber: fromSequenceNumber);

        var result = await _messageOperationsService.PeekMessagesAsync(request, cancellationToken);
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
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(namespaceResult.Error);
        }

        var request = new GetMessagesRequest(
            NamespaceId: namespaceId,
            EntityName: queueName,
            SubscriptionName: null,
            FromDeadLetter: true,
            MaxMessages: Math.Clamp(maxMessages, GetMessagesRequest.MinAllowedMessages, GetMessagesRequest.MaxAllowedMessages),
            FromSequenceNumber: fromSequenceNumber);

        var result = await _messageOperationsService.PeekDeadLetterMessagesAsync(request, cancellationToken);
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
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<MessageResponse>>(namespaceResult.Error);
        }

        var request = new GetMessagesRequest(
            NamespaceId: namespaceId,
            EntityName: topicName,
            SubscriptionName: subscriptionName,
            FromDeadLetter: true,
            MaxMessages: Math.Clamp(maxMessages, GetMessagesRequest.MinAllowedMessages, GetMessagesRequest.MaxAllowedMessages),
            FromSequenceNumber: fromSequenceNumber);

        var result = await _messageOperationsService.PeekDeadLetterMessagesAsync(request, cancellationToken);
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

        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult(Shared.Results.Result.Failure(namespaceResult.Error));
        }

        var ns = namespaceResult.Value;

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

        var result = await _messageOperationsService.ReplayMessageAsync(
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

    /// <summary>
    /// Purges (permanently deletes) a single message from the active or dead-letter queue.
    /// </summary>
    /// <remarks>
    /// Supported for AWS SQS (delete by receipt handle) and GCP Pub/Sub (acknowledge).
    /// Azure Service Bus has no reliable single-message delete; the provider returns an error.
    /// </remarks>
    [RequireScope(ApiKeyScopes.MessagesSend)]
    [HttpDelete("purge")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> PurgeMessage(
        [FromQuery] Guid namespaceId,
        [FromQuery] long sequenceNumber,
        [FromQuery] string entityName,
        [FromQuery] string? subscriptionName = null,
        [FromQuery] bool fromDeadLetter = false,
        CancellationToken cancellationToken = default)
    {
        if (!IntentHeaders.HasExplicitIntent(HttpContext, IntentHeaders.IntentPurgeMessage))
        {
            _auditLogger.LogCriticalAction(
                HttpContext,
                OwnerId,
                action: IntentHeaders.IntentPurgeMessage,
                outcome: "Denied",
                namespaceId: namespaceId,
                resourceName: entityName,
                sequenceNumber: sequenceNumber,
                detail: "Missing explicit intent headers");

            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Explicit Intent Required",
                detail: IntentHeaders.BuildIntentRequiredDetail("message purge"));
        }

        _logger.LogInformation(
            "Purging message {SequenceNumber} from {EntityName} (deadLetter: {FromDeadLetter}) in namespace {NamespaceId}",
            sequenceNumber,
            LogRedactor.SanitiseForLog(entityName),
            fromDeadLetter,
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

        // Safety-by-default guard: destructive purge is blocked in production.
        if (ns.Environment == EnvironmentType.Prod)
        {
            _auditLogger.LogCriticalAction(
                HttpContext,
                OwnerId,
                action: IntentHeaders.IntentPurgeMessage,
                outcome: "Denied",
                namespaceId: namespaceId,
                environment: ns.Environment,
                resourceName: entityName,
                sequenceNumber: sequenceNumber,
                detail: "Purge blocked in production environment");

            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Production Restriction",
                detail: "Purge is blocked for production namespaces. Validate in DEV and UAT first.");
        }

        var result = await _messageOperationsService.PurgeMessageAsync(
            namespaceId,
            entityName,
            subscriptionName,
            sequenceNumber,
            fromDeadLetter,
            cancellationToken);

        if (result.IsFailure)
        {
            _auditLogger.LogCriticalAction(
                HttpContext,
                OwnerId,
                action: IntentHeaders.IntentPurgeMessage,
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
            action: IntentHeaders.IntentPurgeMessage,
            outcome: "Succeeded",
            namespaceId: namespaceId,
            environment: ns.Environment,
            resourceName: entityName,
            sequenceNumber: sequenceNumber,
            detail: "Purge completed");

        _logger.LogInformation("Message {SequenceNumber} purged successfully", sequenceNumber);
        return Accepted();
    }

    /// <summary>
    /// Streams newly-arrived messages for one queue or subscription as Server-Sent Events —
    /// "tail -f" for live incident debugging. Read-only: uses the same
    /// <see cref="IMessageOperationsService.PeekMessagesAsync"/> every other message view
    /// uses, just on a short repeating interval, so it is safe on Production namespaces.
    /// </summary>
    /// <remarks>
    /// Not available for providers whose peek isn't safe to repeat (see
    /// <see cref="Core.Models.ProviderCapabilities.SupportsRepeatablePeek"/>) — AWS SQS has no
    /// non-destructive peek, so polling it on an interval would push messages toward their
    /// queue's <c>maxReceiveCount</c> and risk accidental dead-lettering.
    /// </remarks>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="entityName">The queue or topic name.</param>
    /// <param name="subscriptionName">Optional subscription name for topic subscriptions.</param>
    /// <param name="fromDeadLetter">Whether to tail the dead-letter queue instead of the active entity.</param>
    /// <param name="cancellationToken">Bound to the request abort token.</param>
    /// <response code="200">Stream opened.</response>
    /// <response code="400">Missing entity name.</response>
    /// <response code="404">Namespace not found, not owned by the caller, or its provider is not registered.</response>
    /// <response code="409">The namespace's provider does not support repeatable peek (AWS).</response>
    /// <response code="503">The concurrent Live Tail session cap has been reached.</response>
    [RequireScope(ApiKeyScopes.MessagesPeek)]
    [HttpGet("live-tail")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task LiveTail(
        [FromQuery] Guid namespaceId,
        [FromQuery] string entityName,
        [FromQuery] string? subscriptionName,
        [FromQuery] bool fromDeadLetter,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityName))
        {
            Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var ns = namespaceResult.Value;
        if (!_providerRouter.IsRegistered(ns.Provider))
        {
            Response.StatusCode = StatusCodes.Status404NotFound;
            return;
        }

        var provider = _providerRouter.Resolve(ns.Provider);
        if (!provider.Capabilities.SupportsRepeatablePeek)
        {
            Response.StatusCode = StatusCodes.Status409Conflict;
            return;
        }

        if (!_liveTailConnectionLimiter.TryAcquire())
        {
            Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            return;
        }

        try
        {
            var session = _liveTailSessionFactory.Create(namespaceId, entityName, subscriptionName, fromDeadLetter);

            Response.Headers.ContentType = "text/event-stream; charset=utf-8";
            Response.Headers.CacheControl = "no-cache, no-store";
            Response.Headers["X-Accel-Buffering"] = "no";
            HttpContext.Features.Get<IHttpResponseBodyFeature>()?.DisableBuffering();

            await WriteAndFlushAsync(": connected\n\n", cancellationToken);

            var sessionStartUtc = DateTimeOffset.UtcNow;

            while (!cancellationToken.IsCancellationRequested)
            {
                if (DateTimeOffset.UtcNow - sessionStartUtc > LiveTailMaxSessionDuration)
                {
                    await WriteAndFlushAsync(": session-expired\n\n", cancellationToken);
                    break;
                }

                var pollResult = await session.PollNextAsync(cancellationToken);

                if (pollResult.IsFailure)
                {
                    _logger.LogWarning(
                        "Live Tail poll failed for namespace {NamespaceId} entity {EntityName}: {Error}",
                        namespaceId,
                        LogRedactor.SanitiseForLog(entityName),
                        pollResult.Error.Message);
                    await WriteAndFlushAsync(": poll-error\n\n", cancellationToken);
                }
                else if (pollResult.Value.Count > 0)
                {
                    foreach (var message in pollResult.Value)
                    {
                        var payload = JsonSerializer.Serialize(MapToResponse(message), LiveTailSerializerOptions);
                        await WriteAndFlushAsync($"data: {payload}\n\n", cancellationToken);
                    }
                }
                else
                {
                    await WriteAndFlushAsync(": heartbeat\n\n", cancellationToken);
                }

                await Task.Delay(LiveTailPollInterval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Client disconnected — normal SSE lifecycle, not an error.
        }
        finally
        {
            _liveTailConnectionLimiter.Release();
        }
    }

    private async Task WriteAndFlushAsync(string frame, CancellationToken cancellationToken)
    {
        await Response.WriteAsync(frame, cancellationToken);
        await Response.Body.FlushAsync(cancellationToken);
    }
}
