using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for managing Service Bus subscriptions.
/// Provides endpoints for listing subscriptions and their metadata.
/// </summary>
[Route(ApiRoutes.Subscriptions.Base)]
[Tags("Subscriptions")]
public sealed class SubscriptionsController : ApiControllerBase
{
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IServiceBusClientCache _clientCache;
    private readonly IConnectionStringProtector _connectionStringProtector;
    private readonly ILogger<SubscriptionsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionsController"/> class.
    /// </summary>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="connectionStringProtector">The connection string protector.</param>
    /// <param name="logger">The logger.</param>
    public SubscriptionsController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        ILogger<SubscriptionsController> logger)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets all subscriptions for a topic.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="topicName">The topic name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of subscription information.</returns>
    /// <response code="200">Subscriptions retrieved successfully.</response>
    /// <response code="404">Namespace or topic not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionRuntimePropertiesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionRuntimePropertiesDto>>> GetAll(
        [FromQuery] Guid namespaceId,
        [FromQuery] string topicName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Getting all subscriptions for topic {TopicName} in namespace {NamespaceId}",
            topicName,
            namespaceId);

        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<SubscriptionRuntimePropertiesDto>>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;
        if (ns.ConnectionString is null)
        {
            return BadRequest("Namespace does not have a connection string configured.");
        }

        var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<SubscriptionRuntimePropertiesDto>>(unprotectResult.Error);
        }

        var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
        var subscriptionsResult = await wrapper.GetSubscriptionsAsync(topicName, cancellationToken);
        if (subscriptionsResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<SubscriptionRuntimePropertiesDto>>(subscriptionsResult.Error);
        }

        _logger.LogInformation(
            "Retrieved {SubscriptionCount} subscriptions for topic {TopicName} in namespace {NamespaceId}",
            subscriptionsResult.Value.Count,
            topicName,
            namespaceId);

        return Ok(subscriptionsResult.Value);
    }

    /// <summary>
    /// Gets information about a specific subscription.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="topicName">The topic name.</param>
    /// <param name="subscriptionName">The subscription name.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The subscription information.</returns>
    /// <response code="200">Subscription retrieved successfully.</response>
    /// <response code="404">Namespace, topic, or subscription not found.</response>
    /// <response code="502">Service Bus communication error.</response>
    [HttpGet("{subscriptionName}")]
    [ProducesResponseType(typeof(SubscriptionRuntimePropertiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SubscriptionRuntimePropertiesDto>> GetByName(
        [FromQuery] Guid namespaceId,
        [FromQuery] string topicName,
        [FromRoute] string subscriptionName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Getting subscription {SubscriptionName} for topic {TopicName} in namespace {NamespaceId}",
            subscriptionName,
            topicName,
            namespaceId);

        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<SubscriptionRuntimePropertiesDto>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;
        if (ns.ConnectionString is null)
        {
            return BadRequest("Namespace does not have a connection string configured.");
        }

        var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
        {
            return ToActionResult<SubscriptionRuntimePropertiesDto>(unprotectResult.Error);
        }

        var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
        var subscriptionResult = await wrapper.GetSubscriptionAsync(topicName, subscriptionName, cancellationToken);
        if (subscriptionResult.IsFailure)
        {
            return ToActionResult<SubscriptionRuntimePropertiesDto>(subscriptionResult.Error);
        }

        return Ok(subscriptionResult.Value);
    }
}
