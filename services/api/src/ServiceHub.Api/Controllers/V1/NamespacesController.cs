using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using ServiceHub.Api.Authorization;
using ServiceHub.Infrastructure.Configuration;
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
    private readonly EntraIdOptions _entraIdOptions;
    private readonly IOAuthService _oauthService;
    private readonly ILogger<NamespacesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NamespacesController"/> class.
    /// </summary>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="clientFactory">The Service Bus client factory.</param>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="connectionStringProtector">The connection string protector.</param>
    /// <param name="entraIdOptions">The Entra ID configuration options.</param>
    /// <param name="oauthService">The OAuth service for user-delegated auth.</param>
    /// <param name="logger">The logger.</param>
    public NamespacesController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientFactory clientFactory,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        IOptions<EntraIdOptions> entraIdOptions,
        IOAuthService oauthService,
        ILogger<NamespacesController> logger)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _entraIdOptions = (entraIdOptions ?? throw new ArgumentNullException(nameof(entraIdOptions))).Value;
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
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

        // Check for existing namespace with same name
        var existingResult = await _namespaceRepository.GetByNameAsync(request.Name, cancellationToken);
        if (existingResult.IsSuccess)
        {
            var error = Error.Conflict(
                ErrorCodes.Namespace.AlreadyExists,
                $"A namespace with name '{request.Name}' already exists.");
            return ToActionResult<NamespaceResponse>(error);
        }

        // Check for duplicate connection string (prevent same connection string with different display names)
        if (request.AuthType == ConnectionAuthType.ConnectionString && !string.IsNullOrEmpty(request.ConnectionString))
        {
            var allNamespacesResult = await _namespaceRepository.GetAllAsync(cancellationToken);
            if (allNamespacesResult.IsSuccess)
            {
                foreach (var existingNs in allNamespacesResult.Value)
                {
                    if (existingNs.ConnectionString is not null)
                    {
                        var unprotectResult = _connectionStringProtector.Unprotect(existingNs.ConnectionString);
                        if (unprotectResult.IsSuccess && 
                            string.Equals(unprotectResult.Value, request.ConnectionString, StringComparison.OrdinalIgnoreCase))
                        {
                            var error = Error.Conflict(
                                ErrorCodes.Namespace.AlreadyExists,
                                $"A namespace with this connection string already exists (Display Name: '{existingNs.DisplayName ?? existingNs.Name}'). Please use a different connection string.");
                            return ToActionResult<NamespaceResponse>(error);
                        }
                    }
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
                request.Environment);
        }
        else if (request.AuthType == ConnectionAuthType.UserDelegated)
        {
            // Read session ID from HttpOnly cookie — never from the request body
            var sessionId = Request.Cookies["servicehub_oauth_session"];
            if (string.IsNullOrEmpty(sessionId))
            {
                return Unauthorized(new { detail = "No active Azure sign-in session. Please sign in with Azure Entra ID first." });
            }

            var sessionInfo = _oauthService.GetSessionInfo(sessionId);
            if (sessionInfo is null || sessionInfo.IsExpired)
            {
                return Unauthorized(new { detail = "Your Azure sign-in session has expired. Please sign in again." });
            }

            // When subscriptionId + resourceGroup are provided, retrieve the SAS connection string
            // from ARM (listKeys). This avoids requiring the https://servicebus.azure.com enterprise
            // app to be provisioned in the tenant, and works for all Azure AD tenants.
            if (!string.IsNullOrEmpty(request.SubscriptionId) && !string.IsNullOrEmpty(request.ResourceGroup))
            {
                // Extract short namespace name from FQDN (e.g. "my-ns.servicebus.windows.net" → "my-ns")
                var shortName = request.Name.Contains('.')
                    ? request.Name.Split('.')[0]
                    : request.Name;

                var connStringResult = await _oauthService.GetConnectionStringAsync(
                    sessionId,
                    request.SubscriptionId,
                    request.ResourceGroup,
                    shortName,
                    cancellationToken);

                if (connStringResult.IsFailure)
                    return ToActionResult<NamespaceResponse>(connStringResult.Error);

                // Encrypt directly — skip the RootManageSharedAccessKey guard because this
                // connection string was retrieved on behalf of an authenticated Owner via ARM.
                var protectedResult = _connectionStringProtector.Protect(connStringResult.Value);
                if (protectedResult.IsFailure)
                    return ToActionResult<NamespaceResponse>(protectedResult.Error);

                createResult = Namespace.Create(
                    request.Name,
                    protectedResult.Value,
                    request.DisplayName,
                    request.Description,
                    request.Environment);
            }
            else
            {
                // Fallback: store as UserDelegated (uses token-credential data-plane path).
                createResult = Namespace.CreateWithUserDelegated(
                    request.Name,
                    sessionId,
                    request.DisplayName,
                    request.Description,
                    request.Environment);
            }
        }
        else
        {
            createResult = Namespace.CreateWithManagedIdentity(
                request.Name,
                request.AuthType,
                request.DisplayName,
                request.Description,
                request.Environment);
        }

        if (createResult.IsFailure)
        {
            return ToActionResult<NamespaceResponse>(createResult.Error);
        }

        var ns = createResult.Value;

        // For Entra ID auth types, validate connectivity before persisting.
        // This ensures the role assignment is in place before the namespace is saved.
        // Skip when ns.AuthType is ConnectionString — this happens when UserDelegated auth
        // retrieved the connection string via ARM listKeys (already validated by ARM).
        if (request.AuthType != ConnectionAuthType.ConnectionString &&
            ns.AuthType != ConnectionAuthType.ConnectionString)
        {
            var createClientResult = await _clientFactory.CreateClientAsync(ns, cancellationToken);
            if (createClientResult.IsFailure)
            {
                return ToActionResult<NamespaceResponse>(createClientResult.Error);
            }
        }

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

    /// <summary>Gets whether Entra ID authentication is available on this instance.</summary>
    [HttpGet("entra-id/status")]
    [ProducesResponseType(typeof(EntraIdStatusResponse), StatusCodes.Status200OK)]
    public IActionResult GetEntraIdStatus()
    {
        var isAvailable = _entraIdOptions.IsAvailable;
        var isDefaultCred = _entraIdOptions.IsDefaultCredentialMode;

        return Ok(new EntraIdStatusResponse(
            IsAvailable: isAvailable,
            ClientId: _entraIdOptions.IsConfigured ? _entraIdOptions.ClientId : null,
            IsDefaultCredentialMode: isDefaultCred,
            Message: _entraIdOptions.IsConfigured
                ? "Azure Entra ID authentication is available. Users can connect without a connection string."
                : isDefaultCred
                    ? "Azure Entra ID is available via DefaultAzureCredential (az login or Managed Identity). No App Registration credentials configured."
                    : "Azure Entra ID is not configured on this instance. Use connection string authentication or self-host with your own App Registration."));
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

        var ns = namespaceResult.Value;

        // For Entra ID namespaces there is no stored connection string;
        // connectivity is validated at registration time and again via CreateClientAsync.
        if (ns.ConnectionString is null)
        {
            if (ns.AuthType != ConnectionAuthType.ConnectionString)
            {
                var entraTestResult = await _clientFactory.CreateClientAsync(ns, cancellationToken);
                var connected = entraTestResult.IsSuccess;

                ns.RecordConnectionTest(connected);
                await _namespaceRepository.UpdateAsync(ns, cancellationToken);

                return Ok(new ConnectionTestResponse(
                    IsConnected: connected,
                    Message: connected
                        ? "Entra ID connection verified successfully."
                        : entraTestResult.Error.Message,
                    TestedAt: DateTimeOffset.UtcNow));
            }

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
    [RequireScope(ApiKeyScopes.NamespacesWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
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
        _logger.LogInformation("Deleting namespace {NamespaceId}", ns.Id);

        if (_clientCache.Contains(ns.Id))
            await _clientCache.RemoveAsync(ns.Id, cancellationToken);

        var result = await _namespaceRepository.DeleteAsync(ns.Id, cancellationToken);
        if (result.IsFailure)
            return ToActionResult(result);

        _logger.LogInformation("Namespace {NamespaceId} deleted successfully", ns.Id);
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
/// Response model for Entra ID availability status.
/// </summary>
/// <param name="IsAvailable">Whether Entra ID authentication is configured and available.</param>
/// <param name="ClientId">ServiceHub's App Registration Client ID (if available).</param>
/// <param name="IsDefaultCredentialMode">True when using DefaultAzureCredential (no explicit App Registration).</param>
/// <param name="Message">Human-readable status message.</param>
public sealed record EntraIdStatusResponse(
    bool IsAvailable,
    string? ClientId,
    bool IsDefaultCredentialMode,
    string Message);
