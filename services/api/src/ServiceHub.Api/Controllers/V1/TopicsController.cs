using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;
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
    private readonly ILogger<TopicsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="TopicsController"/> class.
    /// </summary>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="connectionStringProtector">The connection string protector.</param>
    /// <param name="logger">The logger.</param>
    public TopicsController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        ILogger<TopicsController> logger)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
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
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<TopicRuntimePropertiesDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<IReadOnlyList<TopicRuntimePropertiesDto>>> GetAll(
        [FromQuery] Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting all topics for namespace {NamespaceId}", namespaceId);

        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<TopicRuntimePropertiesDto>>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;
        if (ns.ConnectionString is null)
        {
            return BadRequest("Namespace does not have a connection string configured.");
        }

        var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
        {
            return ToActionResult<IReadOnlyList<TopicRuntimePropertiesDto>>(unprotectResult.Error);
        }

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
    [HttpGet("{topicName}")]
    [ProducesResponseType(typeof(TopicRuntimePropertiesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<TopicRuntimePropertiesDto>> GetByName(
        [FromQuery] Guid namespaceId,
        [FromRoute] string topicName,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Getting topic {TopicName} for namespace {NamespaceId}",
            topicName,
            namespaceId);

        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
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

        var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
        var topicResult = await wrapper.GetTopicAsync(topicName, cancellationToken);
        if (topicResult.IsFailure)
        {
            return ToActionResult<TopicRuntimePropertiesDto>(topicResult.Error);
        }

        return Ok(topicResult.Value);
    }
}
