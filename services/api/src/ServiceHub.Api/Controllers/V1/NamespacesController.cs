using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for managing Service Bus namespaces.
/// Provides endpoints for creating, listing, testing, and deleting namespace configurations.
/// </summary>
[Route(ApiRoutes.Namespaces.Base)]
[Tags("Namespaces")]
public sealed class NamespacesController : ApiControllerBase
{
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IServiceBusClientFactory _clientFactory;
    private readonly IServiceBusClientCache _clientCache;
    private readonly IConnectionStringProtector _connectionStringProtector;
    private readonly ILogger<NamespacesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamespacesController"/> class.
    /// </summary>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="clientFactory">The Service Bus client factory.</param>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="connectionStringProtector">The connection string protector.</param>
    /// <param name="logger">The logger.</param>
    public NamespacesController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientFactory clientFactory,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        ILogger<NamespacesController> logger)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Creates a new namespace configuration.
    /// </summary>
    /// <param name="request">The create namespace request.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The created namespace response.</returns>
    /// <response code="201">Namespace created successfully.</response>
    /// <response code="400">Invalid request parameters.</response>
    /// <response code="409">Namespace with the same name already exists.</response>
    [HttpPost]
    [RequireScope(ApiKeyScopes.NamespacesWrite)]
    [ProducesResponseType(typeof(NamespaceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<NamespaceResponse>> Create(
        [FromBody] CreateNamespaceRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Creating namespace with name {Name}", request.Name);

        // Check for existing namespace with same name
        var existingResult = await _namespaceRepository.GetByNameAsync(request.Name, cancellationToken);
        if (existingResult.IsSuccess)
        {
            var error = Error.Conflict(
                ErrorCodes.Namespace.AlreadyExists,
                $"A namespace with name '{request.Name}' already exists.");
            return ToActionResult<NamespaceResponse>(error);
        }

        // Create the namespace entity based on auth type
        Result<Namespace> createResult;

        if (request.AuthType == ConnectionAuthType.ConnectionString)
        {
            if (string.IsNullOrEmpty(request.ConnectionString))
            {
                return BadRequest("Connection string is required for connection string authentication.");
            }

            // Validate the connection string format BEFORE encryption
            var validationResult = _clientFactory.ValidateConnectionString(request.ConnectionString);
            if (validationResult.IsFailure)
            {
                return ToActionResult<NamespaceResponse>(validationResult.Error);
            }

            // Protect the connection string before storing
            var protectedConnectionStringResult = _connectionStringProtector.Protect(request.ConnectionString);
            if (protectedConnectionStringResult.IsFailure)
            {
                return ToActionResult<NamespaceResponse>(protectedConnectionStringResult.Error);
            }

            createResult = Namespace.Create(
                request.Name,
                protectedConnectionStringResult.Value,
                request.DisplayName,
                request.Description);
        }
        else
        {
            createResult = Namespace.CreateWithManagedIdentity(
                request.Name,
                request.AuthType,
                request.DisplayName,
                request.Description);
        }

        if (createResult.IsFailure)
        {
            return ToActionResult<NamespaceResponse>(createResult.Error);
        }

        var ns = createResult.Value;

        // Save to repository
        var saveResult = await _namespaceRepository.AddAsync(ns, cancellationToken);
        if (saveResult.IsFailure)
        {
            return ToActionResult<NamespaceResponse>(saveResult.Error);
        }

        var response = MapToResponse(ns);

        _logger.LogInformation("Namespace {NamespaceId} created successfully", ns.Id);

        return CreatedAtAction(
            nameof(GetById),
            new { id = ns.Id },
            response);
    }

    /// <summary>
    /// Gets all namespace configurations.
    /// </summary>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of namespace responses.</returns>
    /// <response code="200">Namespaces retrieved successfully.</response>
    [HttpGet]
    [RequireScope(ApiKeyScopes.NamespacesRead)]
    [ProducesResponseType(typeof(IReadOnlyList<NamespaceResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<NamespaceResponse>>> GetAll(
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting all namespaces");

        var result = await _namespaceRepository.GetAllAsync(cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult<IReadOnlyList<NamespaceResponse>>(result.Error);
        }

        var responses = result.Value
            .Select(MapToResponse)
            .ToList();

        return Ok(responses);
    }

    /// <summary>
    /// Gets a namespace configuration by ID.
    /// </summary>
    /// <param name="id">The namespace ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The namespace response.</returns>
    /// <response code="200">Namespace retrieved successfully.</response>
    /// <response code="404">Namespace not found.</response>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NamespaceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NamespaceResponse>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting namespace {NamespaceId}", id);

        var result = await _namespaceRepository.GetByIdAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult<NamespaceResponse>(result.Error);
        }

        var response = MapToResponse(result.Value);
        return Ok(response);
    }

    /// <summary>
    /// Tests the connection to a namespace.
    /// </summary>
    /// <param name="id">The namespace ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The test result.</returns>
    /// <response code="200">Connection test completed.</response>
    /// <response code="404">Namespace not found.</response>
    [HttpGet("{id:guid}/test")]
    [HttpPost("{id:guid}/test-connection")]
    [ProducesResponseType(typeof(ConnectionTestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConnectionTestResponse>> TestConnection(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Testing connection for namespace {NamespaceId}", id);

        var namespaceResult = await _namespaceRepository.GetByIdAsync(id, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<ConnectionTestResponse>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;
        if (ns.ConnectionString is null)
        {
            return Ok(new ConnectionTestResponse(
                IsConnected: false,
                Message: "Namespace does not have a connection string configured.",
                TestedAt: DateTimeOffset.UtcNow));
        }

        var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
        {
            return Ok(new ConnectionTestResponse(
                IsConnected: false,
                Message: $"Failed to decrypt connection string: {unprotectResult.Error.Message}",
                TestedAt: DateTimeOffset.UtcNow));
        }

        // Try to create a client and get basic info
        try
        {
            var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);
            var queuesResult = await wrapper.GetQueuesAsync(cancellationToken);

            if (queuesResult.IsFailure)
            {
                _logger.LogWarning(
                    "Connection test failed for namespace {NamespaceId}: {Error}",
                    id,
                    queuesResult.Error.Message);

                ns.RecordConnectionTest(false);
                await _namespaceRepository.UpdateAsync(ns, cancellationToken);

                return Ok(new ConnectionTestResponse(
                    IsConnected: false,
                    Message: $"Connection test failed: {queuesResult.Error.Message}",
                    TestedAt: DateTimeOffset.UtcNow));
            }

            // Update the namespace with successful test
            ns.RecordConnectionTest(true);
            await _namespaceRepository.UpdateAsync(ns, cancellationToken);

            _logger.LogInformation("Connection test successful for namespace {NamespaceId}", id);

            return Ok(new ConnectionTestResponse(
                IsConnected: true,
                Message: "Connection successful",
                TestedAt: DateTimeOffset.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Connection test failed for namespace {NamespaceId}",
                id);

            // Update the namespace with failed test
            ns.RecordConnectionTest(false);
            await _namespaceRepository.UpdateAsync(ns, cancellationToken);

            return Ok(new ConnectionTestResponse(
                IsConnected: false,
                Message: $"Connection test failed: {ex.Message}",
                TestedAt: DateTimeOffset.UtcNow));
        }
    }

    /// <summary>
    /// Deletes a namespace configuration.
    /// </summary>
    /// <param name="id">The namespace ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>No content on success.</returns>
    /// <response code="204">Namespace deleted successfully.</response>
    /// <response code="404">Namespace not found.</response>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Deleting namespace {NamespaceId}", id);

        // Remove from client cache first
        if (_clientCache.Contains(id))
        {
            await _clientCache.RemoveAsync(id, cancellationToken);
        }

        var result = await _namespaceRepository.DeleteAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult(result);
        }

        _logger.LogInformation("Namespace {NamespaceId} deleted successfully", id);
        return NoContent();
    }

    /// <summary>
    /// Maps a Namespace entity to a NamespaceResponse DTO.
    /// </summary>
    /// <param name="ns">The namespace entity.</param>
    /// <returns>The namespace response.</returns>
    private static NamespaceResponse MapToResponse(Namespace ns)
    {
        return new NamespaceResponse(
            Id: ns.Id,
            Name: ns.Name,
            DisplayName: ns.DisplayName,
            Description: ns.Description,
            AuthType: ns.AuthType,
            IsActive: ns.IsActive,
            CreatedAt: ns.CreatedAt,
            ModifiedAt: ns.ModifiedAt,
            LastConnectionTestAt: ns.LastConnectionTestAt,
            LastConnectionTestSucceeded: ns.LastConnectionTestSucceeded);
    }
}

/// <summary>
/// Response model for connection test results.
/// </summary>
/// <param name="IsConnected">Whether the connection was successful.</param>
/// <param name="Message">The result message.</param>
/// <param name="TestedAt">When the test was performed.</param>
public sealed record ConnectionTestResponse(
    bool IsConnected,
    string Message,
    DateTimeOffset TestedAt);
