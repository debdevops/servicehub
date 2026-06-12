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
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<NamespacesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamespacesController"/> class.
    /// </summary>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="clientFactory">The Service Bus client factory.</param>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="connectionStringProtector">The connection string protector.</param>
    /// <param name="logger">The logger.</param>
    /// <param name="auditLogger">The security audit logger.</param>
    public NamespacesController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientFactory clientFactory,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        ILogger<NamespacesController> logger,
        IAuditLogger? auditLogger = null)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _auditLogger = auditLogger ?? NoOpAuditLogger.Instance;
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
        _logger.LogInformation("Creating namespace with name {Name}", LogRedactor.SanitiseForLog(request.Name));

        // Owner-scoped duplicate checks: only look within the same tenant's pool.
        var ownerNamespaces = await _namespaceRepository.GetByOwnerAsync(OwnerId, cancellationToken);
        if (ownerNamespaces.IsSuccess)
        {
            // Check for existing namespace with same name (within this owner's pool)
            var nameConflict = ownerNamespaces.Value.FirstOrDefault(n =>
                string.Equals(n.Name, request.Name.Trim().ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));
            if (nameConflict is not null)
            {
                var error = Error.Conflict(
                    ErrorCodes.Namespace.AlreadyExists,
                    $"A namespace with name '{request.Name}' already exists.");
                return ToActionResult<NamespaceResponse>(error);
            }

            // Hash-based duplicate connection string check — O(1) hash compare, no decryption needed.
            if (request.AuthType == ConnectionAuthType.ConnectionString && !string.IsNullOrEmpty(request.ConnectionString))
            {
                var incomingHash = Namespace.ComputeConnectionStringHash(request.ConnectionString);
                var connStringConflict = ownerNamespaces.Value.FirstOrDefault(n =>
                    n.ConnectionStringHash is not null &&
                    string.Equals(n.ConnectionStringHash, incomingHash, StringComparison.Ordinal));

                if (connStringConflict is not null)
                {
                    var error = Error.Conflict(
                        ErrorCodes.Namespace.AlreadyExists,
                        $"A namespace with this connection string already exists (Display Name: '{connStringConflict.DisplayName ?? connStringConflict.Name}'). Please use a different connection string.");
                    return ToActionResult<NamespaceResponse>(error);
                }
            }
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

            // Compute hash of plaintextconnection string BEFORE encryption for deduplication
            var connectionStringHash = Namespace.ComputeConnectionStringHash(request.ConnectionString);

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
                request.Description,
                request.Environment,
                ownerId: OwnerId,
                connectionStringHash: connectionStringHash);
        }
        else
        {
            createResult = Namespace.CreateWithManagedIdentity(
                request.Name,
                request.AuthType,
                request.DisplayName,
                request.Description,
                request.Environment,
                ownerId: OwnerId);
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

        // TENANT ISOLATION: Only return namespaces owned by the authenticated caller.
        var result = await _namespaceRepository.GetByOwnerAsync(OwnerId, cancellationToken);
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
    [RequireScope(ApiKeyScopes.NamespacesRead)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NamespaceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NamespaceResponse>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        // When authentication is enabled, enforce read scope in-method so
        // static-analysis tools can trace the authorization check before the data access.
        // SPA token auth gets full access — scope restrictions only apply to API keys.
        if (HttpContext.Items.ContainsKey("Authenticated") &&
            HttpContext.Items["AuthMethod"] is not "SpaToken" &&
            (!HttpContext.Items.TryGetValue("ApiKeyConfig", out var keyConfigObj) ||
             keyConfigObj is not ApiKeyConfiguration keyConfig ||
             !keyConfig.HasScope(ApiKeyScopes.NamespacesRead)))
        {
            return Forbid();
        }

        _logger.LogInformation("Getting namespace {NamespaceId}", id);

        var result = await _namespaceRepository.GetByIdAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult<NamespaceResponse>(result.Error);
        }

        // TENANT ISOLATION: Return 404 (not 403) when the namespace exists but belongs
        // to a different owner, to avoid leaking that the ID is in use.
        if (!string.Equals(result.Value.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return ToActionResult<NamespaceResponse>(Error.NotFound(
                ErrorCodes.Namespace.NotFound,
                $"Namespace with ID '{id}' was not found."));
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
    [RequireScope(ApiKeyScopes.NamespacesRead)]
    [HttpGet("{id:guid}/test")]
    [HttpPost("{id:guid}/test-connection")]
    [ProducesResponseType(typeof(ConnectionTestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ConnectionTestResponse>> TestConnection(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        // When authentication is enabled, enforce read scope in-method so
        // static-analysis tools can trace the authorization check before the data access.
        // SPA token auth gets full access — scope restrictions only apply to API keys.
        if (HttpContext.Items.ContainsKey("Authenticated") &&
            HttpContext.Items["AuthMethod"] is not "SpaToken" &&
            (!HttpContext.Items.TryGetValue("ApiKeyConfig", out var keyConfigObj) ||
             keyConfigObj is not ApiKeyConfiguration keyConfig ||
             !keyConfig.HasScope(ApiKeyScopes.NamespacesRead)))
        {
            return Forbid();
        }

        _logger.LogInformation("Testing connection for namespace {NamespaceId}", id);

        var namespaceResult = await _namespaceRepository.GetByIdAsync(id, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<ConnectionTestResponse>(namespaceResult.Error);
        }

        // TENANT ISOLATION: Return 404 when the namespace exists but belongs to a different owner.
        if (!string.Equals(namespaceResult.Value.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return ToActionResult<ConnectionTestResponse>(Error.NotFound(
                ErrorCodes.Namespace.NotFound,
                $"Namespace with ID '{id}' was not found."));
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
                "Connection test exception for {Id}",
                id);

            // Update the namespace with failed test
            ns.RecordConnectionTest(false);
            await _namespaceRepository.UpdateAsync(ns, cancellationToken);

            return Ok(new ConnectionTestResponse(
                IsConnected: false,
                Message: "An error occurred while testing the connection.",
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
    [RequireScope(ApiKeyScopes.NamespacesWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        if (!IntentHeaders.HasExplicitIntent(HttpContext, IntentHeaders.IntentDeleteNamespace))
        {
            _auditLogger.LogCriticalAction(
                HttpContext,
                OwnerId,
                action: IntentHeaders.IntentDeleteNamespace,
                outcome: "Denied",
                namespaceId: id,
                detail: "Missing explicit intent headers");

            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Explicit Intent Required",
                detail: IntentHeaders.BuildIntentRequiredDetail("namespace deletion"));
        }

        // When authentication is enabled, enforce write scope in-method in addition to
        // the [RequireScope] filter, so the check is visible to static-analysis tools.
        // SPA token auth gets full access — scope restrictions only apply to API keys.
        if (HttpContext.Items.ContainsKey("Authenticated") &&
            HttpContext.Items["AuthMethod"] is not "SpaToken" &&
            (!HttpContext.Items.TryGetValue("ApiKeyConfig", out var keyConfigObj) ||
             keyConfigObj is not ApiKeyConfiguration keyConfig ||
             !keyConfig.HasScope(ApiKeyScopes.NamespacesWrite)))
        {
            return Forbid();
        }

        // Verify the namespace exists first; subsequent operations use the
        // repository-verified entity ID, not the raw user-supplied route value.
        var getResult = await _namespaceRepository.GetByIdAsync(id, cancellationToken);
        if (getResult.IsFailure)
            return ToActionResult(Result.Failure(getResult.Error));

        var ns = getResult.Value;

        // TENANT ISOLATION: Return 404 when namespace exists but belongs to a different owner.
        if (!string.Equals(ns.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return ToActionResult(Result.Failure(Error.NotFound(
                ErrorCodes.Namespace.NotFound,
                $"Namespace with ID '{id}' was not found.")));
        }

        _logger.LogInformation("Deleting namespace {NamespaceId}", ns.Id);
        _auditLogger.LogCriticalAction(
            HttpContext,
            OwnerId,
            action: IntentHeaders.IntentDeleteNamespace,
            outcome: "Attempt",
            namespaceId: ns.Id,
            environment: ns.Environment,
            resourceName: ns.Name,
            detail: "Namespace deletion requested");

        if (_clientCache.Contains(ns.Id))
            await _clientCache.RemoveAsync(ns.Id, cancellationToken);

        var result = await _namespaceRepository.DeleteAsync(ns.Id, cancellationToken);
        if (result.IsFailure)
        {
            _auditLogger.LogCriticalAction(
                HttpContext,
                OwnerId,
                action: IntentHeaders.IntentDeleteNamespace,
                outcome: "Failed",
                namespaceId: ns.Id,
                environment: ns.Environment,
                resourceName: ns.Name,
                detail: result.Error.Message);
            return ToActionResult(result);
        }

        _auditLogger.LogCriticalAction(
            HttpContext,
            OwnerId,
            action: IntentHeaders.IntentDeleteNamespace,
            outcome: "Succeeded",
            namespaceId: ns.Id,
            environment: ns.Environment,
            resourceName: ns.Name,
            detail: "Namespace deleted");

        _logger.LogInformation("Namespace {NamespaceId} deleted successfully", ns.Id);
        return NoContent();
    }

    /// <summary>
    /// Gets aggregate statistics for a namespace, including both queue and subscription DLQ counts.
    /// </summary>
    /// <param name="id">The namespace ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregate stats across queues and topic subscriptions.</returns>
    [HttpGet("{id:guid}/stats")]
    [RequireScope(ApiKeyScopes.MessagesPeek)]
    [ProducesResponseType(typeof(NamespaceStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NamespaceStatsResponse>> GetStats(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(id, cancellationToken);
        if (nsResult.IsFailure)
            return ToActionResult<NamespaceStatsResponse>(nsResult.Error);

        var ns = nsResult.Value;
        if (!string.Equals(ns.OwnerId, OwnerId, StringComparison.Ordinal))
            return NotFound();

        if (string.IsNullOrEmpty(ns.ConnectionString))
            return Ok(new NamespaceStatsResponse(0, 0, 0, 0, 0, 0));

        var unprotectResult = _connectionStringProtector.Unprotect(ns.ConnectionString);
        if (unprotectResult.IsFailure)
            return Ok(new NamespaceStatsResponse(0, 0, 0, 0, 0, 0));

        try
        {
            var wrapper = _clientCache.GetOrCreate(ns.Id, unprotectResult.Value);

            long totalActive = 0, totalDlq = 0, totalScheduled = 0;
            int totalQueues = 0, totalTopics = 0, totalSubscriptions = 0;

            // Aggregate queue stats
            var queuesResult = await wrapper.GetQueuesAsync(cancellationToken);
            if (queuesResult.IsSuccess)
            {
                totalQueues = queuesResult.Value.Count;
                foreach (var q in queuesResult.Value)
                {
                    totalActive += q.ActiveMessageCount;
                    totalDlq += q.DeadLetterMessageCount;
                    totalScheduled += q.ScheduledMessageCount;
                }
            }

            // Aggregate topic subscription stats
            var topicsResult = await wrapper.GetTopicsAsync(cancellationToken);
            if (topicsResult.IsSuccess)
            {
                totalTopics = topicsResult.Value.Count;
                foreach (var topic in topicsResult.Value)
                {
                    var subsResult = await wrapper.GetSubscriptionsAsync(topic.Name, cancellationToken);
                    if (subsResult.IsSuccess)
                    {
                        totalSubscriptions += subsResult.Value.Count;
                        foreach (var sub in subsResult.Value)
                        {
                            totalActive += sub.ActiveMessageCount;
                            totalDlq += sub.DeadLetterMessageCount;
                        }
                    }
                }
            }

            return Ok(new NamespaceStatsResponse(
                TotalQueues: totalQueues,
                TotalTopics: totalTopics,
                TotalSubscriptions: totalSubscriptions,
                TotalActive: totalActive,
                TotalDlq: totalDlq,
                TotalScheduled: totalScheduled));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get stats for namespace {NamespaceId}", id);
            return Ok(new NamespaceStatsResponse(0, 0, 0, 0, 0, 0));
        }
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
            LastConnectionTestSucceeded: ns.LastConnectionTestSucceeded,
            HasListenPermission: ns.HasListenPermission,
            HasSendPermission: ns.HasSendPermission,
            HasManagePermission: ns.HasManagePermission,
            Environment: ns.Environment);
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

/// <summary>
/// Response model for namespace aggregate statistics including both queue and subscription data.
/// </summary>
public sealed record NamespaceStatsResponse(
    int TotalQueues,
    int TotalTopics,
    int TotalSubscriptions,
    long TotalActive,
    long TotalDlq,
    long TotalScheduled);
