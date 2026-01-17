using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.DTOs.Responses;
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
    private readonly ILogger<QueuesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="QueuesController"/> class.
    /// </summary>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="connectionStringProtector">The connection string protector.</param>
    /// <param name="logger">The logger.</param>
    public QueuesController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        ILogger<QueuesController> logger)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
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
        [FromQuery] Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting all queues for namespace {NamespaceId}", namespaceId);

        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<QueueRuntimePropertiesDto>>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;
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
    [HttpGet("{queueName}")]
    [ProducesResponseType(typeof(QueueRuntimePropertiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<QueueRuntimePropertiesDto>> GetByName(
        [FromQuery] Guid namespaceId,
        [FromRoute] string queueName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Getting queue {QueueName} for namespace {NamespaceId}",
            queueName,
            namespaceId);

        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<QueueRuntimePropertiesDto>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;
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
}
