using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Filters;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for managing Service Bus subscriptions.
/// Provides endpoints for listing subscriptions and their metadata.
/// </summary>
[Route(ApiRoutes.Subscriptions.Base)]
[Tags("Subscriptions")]
[RequireNamespaceOwnership]
public sealed class SubscriptionsController : ApiControllerBase
{
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IServiceBusClientCache _clientCache;
    private readonly IConnectionStringProtector _connectionStringProtector;
    private readonly ICloudProviderRouter _providerRouter;
    private readonly ILogger<SubscriptionsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SubscriptionsController"/> class.
    /// </summary>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="connectionStringProtector">The connection string protector.</param>
    /// <param name="providerRouter">Router used to resolve the namespace's cloud provider.</param>
    /// <param name="logger">The logger.</param>
    public SubscriptionsController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        ICloudProviderRouter providerRouter,
        ILogger<SubscriptionsController> logger)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _providerRouter = providerRouter ?? throw new ArgumentNullException(nameof(providerRouter));
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
    [RequireScope(ApiKeyScopes.SubscriptionsRead)]
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<SubscriptionRuntimePropertiesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<SubscriptionRuntimePropertiesDto>>> GetAll(
        [FromRoute] Guid namespaceId,
        [FromRoute] string topicName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Getting all subscriptions for topic {TopicName} in namespace {NamespaceId}",
            LogRedactor.SanitiseForLog(topicName),
            namespaceId);

        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<SubscriptionRuntimePropertiesDto>>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;

        if (ns.Provider != CloudProviderType.Azure)
        {
            return await GetAllViaProviderAsync(ns, topicName, cancellationToken);
        }

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
            LogRedactor.SanitiseForLog(topicName),
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
    [RequireScope(ApiKeyScopes.SubscriptionsRead)]
    [HttpGet("{subscriptionName}")]
    [ProducesResponseType(typeof(SubscriptionRuntimePropertiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<SubscriptionRuntimePropertiesDto>> GetByName(
        [FromRoute] Guid namespaceId,
        [FromRoute] string topicName,
        [FromRoute] string subscriptionName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Getting subscription {SubscriptionName} for topic {TopicName} in namespace {NamespaceId}",
            LogRedactor.SanitiseForLog(subscriptionName),
            LogRedactor.SanitiseForLog(topicName),
            namespaceId);

        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
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

    /// <summary>
    /// Lists subscriptions for non-Azure namespaces via the registered <see cref="ICloudMessagingProvider"/>.
    /// Provider entity snapshots expose subscriptions as "{topicName}/{subscriptionName}" entries
    /// (e.g., AWS SNS fanout endpoints); Azure-specific runtime properties are returned as neutral defaults.
    /// </summary>
    private async Task<ActionResult<IReadOnlyList<SubscriptionRuntimePropertiesDto>>> GetAllViaProviderAsync(
        Core.Entities.Namespace ns,
        string topicName,
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
            return ToActionResult<IReadOnlyList<SubscriptionRuntimePropertiesDto>>(entitiesResult.Error);
        }

        var prefix = topicName + "/";
        var subscriptions = entitiesResult.Value
            .Where(e => string.Equals(e.EntityType, "Subscription", StringComparison.OrdinalIgnoreCase) &&
                        e.Name.StartsWith(prefix, StringComparison.Ordinal))
            .Select(e => new SubscriptionRuntimePropertiesDto(
                Name: e.Name[prefix.Length..],
                TopicName: topicName,
                ActiveMessageCount: e.ActiveMessageCount,
                DeadLetterMessageCount: e.DeadLetterCount,
                TransferMessageCount: 0,
                TransferDeadLetterMessageCount: 0,
                Status: "Active",
                CreatedAt: DateTimeOffset.MinValue,
                UpdatedAt: DateTimeOffset.MinValue,
                AccessedAt: DateTimeOffset.MinValue,
                RequiresSession: false,
                EnableBatchedOperations: false,
                EnableDeadLetteringOnMessageExpiration: false,
                EnableDeadLetteringOnFilterEvaluationExceptions: false,
                MaxDeliveryCount: 0,
                DefaultMessageTimeToLive: TimeSpan.Zero,
                LockDuration: TimeSpan.Zero,
                AutoDeleteOnIdle: TimeSpan.Zero,
                ForwardTo: null,
                ForwardDeadLetteredMessagesTo: null))
            .ToList();

        _logger.LogInformation(
            "Retrieved {SubscriptionCount} subscriptions for topic {TopicName} in namespace {NamespaceId} via {Provider} provider",
            subscriptions.Count,
            LogRedactor.SanitiseForLog(topicName),
            ns.Id,
            ns.Provider);

        return Ok(subscriptions);
    }
}
