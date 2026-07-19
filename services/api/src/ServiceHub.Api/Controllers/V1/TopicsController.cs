using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Security;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for managing Service Bus topics.
/// Provides endpoints for listing topics and their metadata.
/// </summary>
[Route(ApiRoutes.Topics.Base)]
[Tags("Topics")]
public sealed class TopicsController : ApiControllerBase
{
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IServiceBusClientCache _clientCache;
    private readonly IConnectionStringProtector _connectionStringProtector;
    private readonly IMessageOperationsService _messageOperationsService;
    private readonly CloudProviderRouter _providerRouter;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<TopicsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TopicsController"/> class.
    /// </summary>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="connectionStringProtector">The connection string protector.</param>
    /// <param name="messageOperationsService">The provider-aware message operations service.</param>
    /// <param name="providerRouter">Router used to resolve the namespace's cloud provider.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="auditLogger">The security audit logger.</param>
    public TopicsController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        IMessageOperationsService messageOperationsService,
        CloudProviderRouter providerRouter,
        ILogger<TopicsController> logger,
        IAuditLogger? auditLogger = null)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _messageOperationsService = messageOperationsService ?? throw new ArgumentNullException(nameof(messageOperationsService));
        _providerRouter = providerRouter ?? throw new ArgumentNullException(nameof(providerRouter));
        _auditLogger = auditLogger ?? NoOpAuditLogger.Instance;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets all topics for a namespace.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of topic information.</returns>
    /// <response code="200">Topics retrieved successfully.</response>
    /// <response code="404">Namespace not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.TopicsRead)]
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TopicRuntimePropertiesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<TopicRuntimePropertiesDto>>> GetAll(
        [FromRoute] Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting all topics for namespace {NamespaceId}", namespaceId);

        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<TopicRuntimePropertiesDto>>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;

        if (ns.Provider != CloudProviderType.Azure)
        {
            return await GetAllViaProviderAsync(ns, cancellationToken);
        }

        if (ns.ConnectionString is null)
        {
            return BadRequest("Namespace does not have a connection string configured.");
        }

        var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<TopicRuntimePropertiesDto>>(unprotectResult.Error);
        }

        try
        {
            var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
            var topicsResult = await wrapper.GetTopicsAsync(cancellationToken);
            if (topicsResult.IsFailure)
            {
                return ToActionResult<IReadOnlyList<TopicRuntimePropertiesDto>>(topicsResult.Error);
            }

            _logger.LogInformation(
                "Retrieved {TopicCount} topics for namespace {NamespaceId}",
                topicsResult.Value.Count,
                namespaceId);

            return Ok(topicsResult.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to Service Bus namespace {NamespaceId}", namespaceId);
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Service Bus Communication Error",
                Detail = $"Unable to connect to the Service Bus namespace. Verify the connection string is valid and the namespace is reachable. ({ex.GetType().Name})"
            });
        }
    }

    /// <summary>
    /// Lists topics for non-Azure namespaces via the registered <see cref="ICloudMessagingProvider"/>.
    /// Matches any "*Topic" entity type ("SNS Topic" on AWS, "Topic" on GCP); provider entity
    /// snapshots only carry name and message counts, so Azure-specific runtime properties are
    /// returned as neutral defaults.
    /// </summary>
    private async Task<ActionResult<IReadOnlyList<TopicRuntimePropertiesDto>>> GetAllViaProviderAsync(
        Core.Entities.Namespace ns,
        CancellationToken cancellationToken)
    {
        if (!_providerRouter.IsRegistered(ns.Provider))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Provider not enabled",
                Detail = $"The '{ns.Provider}' cloud provider is not enabled on this server. " +
                         $"Set 'CloudProviders:{ns.Provider}:Enabled' to 'true' in appsettings and restart.",
                Instance = HttpContext.Request.Path
            });
        }

        var entitiesResult = await _providerRouter
            .Resolve(ns.Provider)
            .ListEntitiesAsync(ns.Id, cancellationToken);
        if (entitiesResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<TopicRuntimePropertiesDto>>(entitiesResult.Error);
        }

        var entities = entitiesResult.Value;
        var topics = entities
            .Where(e => e.EntityType.EndsWith("Topic", StringComparison.OrdinalIgnoreCase))
            .Select(e => new TopicRuntimePropertiesDto(
                Name: e.Name,
                SubscriptionCount: entities.Count(s =>
                    string.Equals(s.EntityType, "Subscription", StringComparison.OrdinalIgnoreCase) &&
                    s.Name.StartsWith(e.Name + "/", StringComparison.Ordinal)),
                SizeInBytes: 0,
                Status: "Active",
                CreatedAt: DateTimeOffset.MinValue,
                UpdatedAt: DateTimeOffset.MinValue,
                AccessedAt: DateTimeOffset.MinValue,
                RequiresDuplicateDetection: false,
                EnablePartitioning: false,
                EnableBatchedOperations: false,
                SupportOrdering: false,
                MaxSizeInMegabytes: 0,
                DefaultMessageTimeToLive: TimeSpan.Zero,
                AutoDeleteOnIdle: TimeSpan.Zero,
                DuplicateDetectionHistoryTimeWindow: TimeSpan.Zero))
            .ToList();

        _logger.LogInformation(
            "Retrieved {TopicCount} topics for namespace {NamespaceId} via {Provider} provider",
            topics.Count,
            ns.Id,
            ns.Provider);

        return Ok(topics);
    }

    /// <summary>
    /// Gets information about a specific topic.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="topicName">The topic name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The topic information.</returns>
    /// <response code="200">Topic retrieved successfully.</response>
    /// <response code="404">Namespace or topic not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.TopicsRead)]
    [HttpGet("{topicName}")]
    [ProducesResponseType(typeof(TopicRuntimePropertiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<TopicRuntimePropertiesDto>> GetByName(
        [FromRoute] Guid namespaceId,
        [FromRoute] string topicName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Getting topic {TopicName} for namespace {NamespaceId}",
            LogRedactor.SanitiseForLog(topicName),
            namespaceId);

        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<TopicRuntimePropertiesDto>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;

        if (ns.ConnectionString is null)
        {
            return BadRequest("Namespace does not have a connection string configured.");
        }

        var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
        {
            return ToActionResult<TopicRuntimePropertiesDto>(unprotectResult.Error);
        }

        try
        {
            var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
            var topicResult = await wrapper.GetTopicAsync(topicName, cancellationToken);
            if (topicResult.IsFailure)
            {
                return ToActionResult<TopicRuntimePropertiesDto>(topicResult.Error);
            }

            return Ok(topicResult.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error connecting to Service Bus namespace {NamespaceId}", namespaceId);
            return StatusCode(StatusCodes.Status502BadGateway, new ProblemDetails
            {
                Status = StatusCodes.Status502BadGateway,
                Title = "Service Bus Communication Error",
                Detail = $"Unable to connect to the Service Bus namespace. Verify the connection string is valid and the namespace is reachable. ({ex.GetType().Name})"
            });
        }
    }

    /// <summary>
    /// Sends a message to a topic.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="topicName">The topic name.</param>
    /// <param name="request">The send message request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>Accepted if successful.</returns>
    /// <response code="202">Message accepted for delivery.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="403">Insufficient permissions.</response>
    /// <response code="404">Namespace or topic not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.MessagesSend)]
    [HttpPost("{topicName}/messages")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SendMessage(
        [FromRoute] Guid namespaceId,
        [FromRoute] string topicName,
        [FromBody] SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IntentHeaders.HasExplicitIntent(HttpContext, IntentHeaders.IntentSendMessage))
        {
            _auditLogger.LogCriticalAction(HttpContext, OwnerId, IntentHeaders.IntentSendMessage, "Denied", namespaceId, resourceName: topicName, detail: "Missing explicit intent headers");
            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Explicit Intent Required",
                detail: IntentHeaders.BuildIntentRequiredDetail("sending messages"));
        }

        _logger.LogInformation(
            "Sending message to topic {TopicName} in namespace {NamespaceId}",
            LogRedactor.SanitiseForLog(topicName),
            namespaceId);

        // Verify namespace exists
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult(Shared.Results.Result.Failure(namespaceResult.Error));
        }

        var ns = namespaceResult.Value;

        // Check if namespace has Send permission (required to send messages)
        if (!ns.HasSendPermission)
        {
            _auditLogger.LogCriticalAction(HttpContext, OwnerId, IntentHeaders.IntentSendMessage, "Denied", namespaceId, ns.Environment, topicName, detail: "Namespace lacks Send permission");
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
            _auditLogger.LogCriticalAction(HttpContext, OwnerId, IntentHeaders.IntentSendMessage, "Denied", namespaceId, ns.Environment, topicName, detail: "Send blocked in production environment");
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Production Restriction",
                detail: "Sending messages directly to production namespaces is not permitted via ServiceHub. " +
                       "Use your CI/CD pipeline or approved tooling for production message operations.");
        }

        // Create a request with the topic name and namespace ID
        var sendRequest = request with
        {
            EntityName = topicName,
            NamespaceId = namespaceId,
            IsTopic = true
        };

        var result = await _messageOperationsService.SendAsync(sendRequest, cancellationToken);
        if (result.IsFailure)
        {
            _auditLogger.LogCriticalAction(HttpContext, OwnerId, IntentHeaders.IntentSendMessage, "Failed", namespaceId, ns.Environment, topicName, detail: result.Error.Message);
            return ToActionResult(result);
        }

        _auditLogger.LogCriticalAction(HttpContext, OwnerId, IntentHeaders.IntentSendMessage, "Succeeded", namespaceId, ns.Environment, topicName, detail: "Message accepted for delivery");

        _logger.LogInformation("Message sent to topic {TopicName}", LogRedactor.SanitiseForLog(topicName));
        return Accepted();
    }

    /// <summary>
    /// Peeks messages from a topic subscription (active or dead-letter).
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="topicName">The topic name.</param>
    /// <param name="subscriptionName">The subscription name.</param>
    /// <param name="queueType">Queue type: active or deadletter.</param>
    /// <param name="skip">Number of items to skip.</param>
    /// <param name="take">Number of items to take (clamped to max 1000).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A paginated list of messages.</returns>
    /// <response code="200">Messages retrieved successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="404">Namespace, topic, or subscription not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.MessagesPeek)]
    [HttpGet("{topicName}/subscriptions/{subscriptionName}/messages")]
    [ProducesResponseType(typeof(PaginatedResponse<MessageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PaginatedResponse<MessageResponse>>> GetSubscriptionMessages(
        [FromRoute] Guid namespaceId,
        [FromRoute] string topicName,
        [FromRoute] string subscriptionName,
        [FromQuery] string queueType = "active",
        [FromQuery] int skip = 0,
        [FromQuery] int take = 100,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Peeking messages from subscription {SubscriptionName} on topic {TopicName} in namespace {NamespaceId}",
            LogRedactor.SanitiseForLog(subscriptionName),
            LogRedactor.SanitiseForLog(topicName),
            namespaceId);

        var fromDeadLetter = string.Equals(queueType, "deadletter", StringComparison.OrdinalIgnoreCase);
        var pageSize = Math.Clamp(take, GetMessagesRequest.MinAllowedMessages, GetMessagesRequest.MaxAllowedMessages);
        var request = new GetMessagesRequest(
            NamespaceId: namespaceId,
            EntityName: topicName,
            SubscriptionName: subscriptionName,
            FromDeadLetter: fromDeadLetter,
            MaxMessages: pageSize,
            FromSequenceNumber: null);

        var result = fromDeadLetter
            ? await _messageOperationsService.PeekDeadLetterMessagesAsync(request, cancellationToken)
            : await _messageOperationsService.PeekMessagesAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<PaginatedResponse<MessageResponse>>(result.Error);
        }

        // Get the actual total count from the provider's entity listing
        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        int totalCount = result.Value.Count; // Default to peeked count

        if (namespaceResult.IsSuccess && _providerRouter.IsRegistered(namespaceResult.Value.Provider))
        {
            try
            {
                var entitiesResult = await _providerRouter
                    .Resolve(namespaceResult.Value.Provider)
                    .ListEntitiesAsync(namespaceId, cancellationToken);
                if (entitiesResult.IsSuccess)
                {
                    var subscriptionPath = $"{topicName}/subscriptions/{subscriptionName}";
                    var subInfo = entitiesResult.Value.FirstOrDefault(e =>
                        string.Equals(e.EntityType, "Subscription", StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(e.Name, subscriptionPath, StringComparison.OrdinalIgnoreCase));
                    if (subInfo is not null)
                    {
                        totalCount = (int)(fromDeadLetter ? subInfo.DeadLetterCount : subInfo.ActiveMessageCount);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get subscription runtime properties for accurate count");
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
    /// Dead-letters messages from a topic subscription.
    /// Moves messages from the active queue to the dead-letter queue for testing purposes.
    /// </summary>
    /// <remarks>
    /// This endpoint is useful for testing DLQ handling without waiting for real failures.
    /// It moves messages from the active subscription to the dead-letter queue.
    /// </remarks>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="topicName">The topic name.</param>
    /// <param name="subscriptionName">The subscription name.</param>
    /// <param name="messageCount">Number of messages to dead-letter (max 10).</param>
    /// <param name="reason">The reason for dead-lettering.</param>
    /// <param name="errorDescription">Optional error description.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The count of dead-lettered messages.</returns>
    /// <response code="200">Messages dead-lettered successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="404">Namespace, topic, or subscription not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [RequireScope(ApiKeyScopes.MessagesSend)]
    [HttpPost("{topicName}/subscriptions/{subscriptionName}/deadletter")]
    [ProducesResponseType(typeof(DeadLetterResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<DeadLetterResponse>> DeadLetterSubscriptionMessages(
        [FromRoute] Guid namespaceId,
        [FromRoute] string topicName,
        [FromRoute] string subscriptionName,
        [FromQuery] int messageCount = 1,
        [FromQuery] string reason = "ManualDeadLetter",
        [FromQuery] string? errorDescription = null,
        CancellationToken cancellationToken = default)
    {
        if (!IntentHeaders.HasExplicitIntent(HttpContext, IntentHeaders.IntentDeadLetter))
        {
            _auditLogger.LogCriticalAction(HttpContext, OwnerId, IntentHeaders.IntentDeadLetter, "Denied", namespaceId, resourceName: $"{topicName}/subscriptions/{subscriptionName}", detail: "Missing explicit intent headers");
            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Explicit Intent Required",
                detail: IntentHeaders.BuildIntentRequiredDetail("dead-letter operations"));
        }

        _logger.LogInformation(
            "Dead-lettering {Count} messages from subscription {SubscriptionName} on topic {TopicName} in namespace {NamespaceId} with reason: {Reason}",
            messageCount,
            LogRedactor.SanitiseForLog(subscriptionName),
            LogRedactor.SanitiseForLog(topicName),
            namespaceId,
            LogRedactor.SanitiseForLog(reason));

        // Get namespace to check permissions
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<DeadLetterResponse>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;
        
        // Check if namespace has Send permission (required to dead-letter messages)
        if (!ns.HasSendPermission)
        {
            _auditLogger.LogCriticalAction(HttpContext, OwnerId, IntentHeaders.IntentDeadLetter, "Denied", namespaceId, ns.Environment, $"{topicName}/subscriptions/{subscriptionName}", detail: "Namespace lacks Send permission");
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
            _auditLogger.LogCriticalAction(HttpContext, OwnerId, IntentHeaders.IntentDeadLetter, "Denied", namespaceId, ns.Environment, $"{topicName}/subscriptions/{subscriptionName}", detail: "Dead-letter blocked in production environment");
            return Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Production Restriction",
                detail: "Dead-lettering messages in production namespaces is not permitted via ServiceHub. " +
                       "Use your CI/CD pipeline or approved tooling for production message operations.");
        }

        var request = new DeadLetterRequest(
            NamespaceId: namespaceId,
            EntityName: topicName,
            SubscriptionName: subscriptionName,
            MessageCount: messageCount,
            Reason: reason,
            ErrorDescription: errorDescription);

        var result = await _messageOperationsService.DeadLetterMessagesAsync(request, cancellationToken);

        if (result.IsFailure)
        {
            _auditLogger.LogCriticalAction(HttpContext, OwnerId, IntentHeaders.IntentDeadLetter, "Failed", namespaceId, ns.Environment, $"{topicName}/subscriptions/{subscriptionName}", detail: result.Error.Message);
            return ToActionResult<DeadLetterResponse>(result.Error);
        }

        _auditLogger.LogCriticalAction(HttpContext, OwnerId, IntentHeaders.IntentDeadLetter, "Succeeded", namespaceId, ns.Environment, $"{topicName}/subscriptions/{subscriptionName}", detail: $"Dead-lettered {result.Value} messages");

        _logger.LogInformation(
            "Successfully dead-lettered {Count} messages from subscription {SubscriptionName} on topic {TopicName}",
            result.Value,
            LogRedactor.SanitiseForLog(subscriptionName),
            LogRedactor.SanitiseForLog(topicName));

        return Ok(new DeadLetterResponse(result.Value, reason));
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
