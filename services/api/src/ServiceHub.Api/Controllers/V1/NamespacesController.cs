using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Security;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Validation;
using ServiceHub.Infrastructure.Aws;
using ServiceHub.Infrastructure.Gcp;
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
    private readonly IPlatformEventBus? _eventBus;
    private readonly IEnumerable<ICloudMessagingProvider> _messagingProviders;
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
    /// <param name="eventBus">The platform event bus. Optional — omitted in test contexts.</param>
    /// <param name="messagingProviders">The registered cloud messaging providers, used to reject
    /// creation of namespaces for providers that are not enabled on this server.</param>
    public NamespacesController(
        INamespaceRepository namespaceRepository,
        IServiceBusClientFactory clientFactory,
        IServiceBusClientCache clientCache,
        IConnectionStringProtector connectionStringProtector,
        ILogger<NamespacesController> logger,
        IAuditLogger? auditLogger = null,
        IPlatformEventBus? eventBus = null,
        IEnumerable<ICloudMessagingProvider>? messagingProviders = null)
    {
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _auditLogger = auditLogger ?? NoOpAuditLogger.Instance;
        _eventBus = eventBus;
        _messagingProviders = messagingProviders ?? [];
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets the correlation ID for the current request from the HTTP context.
    /// Set by <c>CorrelationIdMiddleware</c> into <c>HttpContext.Items["CorrelationId"]</c>.
    /// Returns null when running outside a full HTTP pipeline (e.g. unit tests).
    /// </summary>
    private string? CorrelationId =>
        HttpContext.Items.TryGetValue("CorrelationId", out var v) && v is string s ? s : null;

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

        // Auth types whose credential material is stored (encrypted) in ConnectionString:
        // Azure connection strings, AWS access key pairs, GCP service account JSON keys.
        var storesCredential = request.AuthType is ConnectionAuthType.ConnectionString
            or ConnectionAuthType.AwsAccessKey
            or ConnectionAuthType.GcpServiceAccount;

        if (request.AuthType == ConnectionAuthType.AwsAccessKey && request.Provider != CloudProviderType.Aws)
        {
            return ToActionResult<NamespaceResponse>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "AuthType 'AwsAccessKey' is only valid when Provider is Aws."));
        }

        if (request.AuthType == ConnectionAuthType.GcpServiceAccount && request.Provider != CloudProviderType.Gcp)
        {
            return ToActionResult<NamespaceResponse>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "AuthType 'GcpServiceAccount' is only valid when Provider is Gcp."));
        }

        // Reject namespaces for providers that are not registered on this server, so the
        // API cannot mint namespace records nothing can serve. Azure is always registered.
        if (request.Provider is CloudProviderType.Aws or CloudProviderType.Gcp
            && !_messagingProviders.Any(p => p.ProviderType == request.Provider))
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Provider not enabled",
                Detail = $"The '{request.Provider}' cloud provider is not enabled on this server. " +
                         $"Set 'CloudProviders:{request.Provider}:Enabled' to 'true' in appsettings and restart.",
                Instance = HttpContext.Request.Path
            });
        }

        // Owner-scoped duplicate checks: only look within the same tenant's pool. Deliberately
        // not narrowed by AllowedNamespaceIds — this is a conflict check against the full owner
        // pool, not a response payload, so a restricted key still gets a clean Conflict instead
        // of creating a namespace whose name collides with one it can't see.
        var ownerNamespaces = await _namespaceRepository.GetByOwnerAsync(OwnerId, allowedNamespaceIds: null, cancellationToken);
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
            if (storesCredential && !string.IsNullOrEmpty(request.ConnectionString))
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

        if (storesCredential)
        {
            if (string.IsNullOrEmpty(request.ConnectionString))
            {
                return BadRequest("Connection string is required for connection string authentication.");
            }

            // Validate the plaintext credential format BEFORE encryption, branched by provider:
            // Azure keeps the Service Bus connection string check; AWS and GCP validate their
            // own credential formats with the same rigor.
            var validationResult = request.Provider switch
            {
                CloudProviderType.Aws => CloudCredentialValidator.ValidateAwsAccessKeyPair(request.ConnectionString),
                CloudProviderType.Gcp => CloudCredentialValidator.ValidateGcpServiceAccountJson(request.ConnectionString),
                _ => _clientFactory.ValidateConnectionString(request.ConnectionString),
            };
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
                request.Provider,
                ownerId: OwnerId,
                connectionStringHash: connectionStringHash,
                awsRegion: request.AwsRegion,
                gcpProjectId: request.GcpProjectId);
        }
        else
        {
            createResult = Namespace.CreateWithManagedIdentity(
                request.Name,
                request.AuthType,
                request.DisplayName,
                request.Description,
                request.Environment,
                request.Provider,
                ownerId: OwnerId,
                awsRegion: request.AwsRegion,
                gcpProjectId: request.GcpProjectId);
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

        _auditLogger.LogCriticalAction(
            HttpContext,
            OwnerId,
            action: "Namespace.Create",
            outcome: "Succeeded",
            namespaceId: ns.Id,
            environment: ns.Environment,
            resourceName: ns.Name);

        // Publish after commit — only fires when AddAsync has durably succeeded.
        if (_eventBus is not null)
        {
            var payload = new NamespaceCreatedPayload
            {
                NamespaceId = ns.Id,
                NamespaceName = ns.Name,
                DisplayName = ns.DisplayName,
                CloudProvider = ns.Provider.ToString().ToLowerInvariant(),
                AuthType = ns.AuthType.ToString(),
                OwnerId = ns.OwnerId,
            };

            var evt = new PlatformEvent
            {
                Source = "ServiceHub.Api.Controllers.V1.NamespacesController",
                Category = EventCategories.Namespace,
                EventType = EventTypes.NamespaceCreated,
                Severity = EventSeverity.Info,
                CloudProvider = ns.Provider.ToString().ToLowerInvariant(),
                NamespaceId = ns.Id,
                NamespaceName = ns.Name,
                CorrelationId = CorrelationId,
                Actor = OwnerId,
                TargetScope = ns.Name,
                Payload = payload,
            };

            await _eventBus.PublishAsync(evt, cancellationToken);

            _logger.LogDebug(
                "Published Platform Event {EventType} for NamespaceId {NamespaceId} CorrelationId {CorrelationId}",
                evt.EventType, ns.Id, evt.CorrelationId);
        }

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

        // TENANT ISOLATION: Only return namespaces owned by the authenticated caller, further
        // narrowed by the caller's namespace allow-list when its credential carries one.
        var result = await _namespaceRepository.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
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

        // TENANT ISOLATION: Returns 404 (not 403) when the namespace exists but belongs
        // to a different owner, to avoid leaking that the ID is in use.
        var result = await GetOwnedNamespaceAsync(_namespaceRepository, id, cancellationToken);
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

        // TENANT ISOLATION: Returns 404 when the namespace exists but belongs to a different owner.
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, id, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<ConnectionTestResponse>(namespaceResult.Error);
        }

        var ns = namespaceResult.Value;

        // AWS/GCP have their own ICloudMessagingProvider.ValidateConnectionAsync implementation
        // that resolves credentials (including the connection-string-less IAM-role/ADC auth
        // types) internally and issues a cheap live probe (ListQueues/ListSubscriptions).
        // Route to it instead of the Azure-only, connection-string-only path below.
        if (ns.Provider is CloudProviderType.Aws or CloudProviderType.Gcp)
        {
            var provider = _messagingProviders.FirstOrDefault(p => p.ProviderType == ns.Provider);
            if (provider is null)
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

            var validateResult = await provider.ValidateConnectionAsync(ns, cancellationToken);
            ns.RecordConnectionTest(validateResult.IsSuccess);
            await _namespaceRepository.UpdateAsync(ns, cancellationToken);

            if (validateResult.IsFailure)
            {
                _logger.LogWarning(
                    "Connection test failed for namespace {NamespaceId}: {Error}",
                    id,
                    validateResult.Error.Message);

                return Ok(new ConnectionTestResponse(
                    IsConnected: false,
                    Message: $"Connection test failed: {validateResult.Error.Message}",
                    TestedAt: DateTimeOffset.UtcNow));
            }

            _logger.LogInformation("Connection test successful for namespace {NamespaceId}", id);

            return Ok(new ConnectionTestResponse(
                IsConnected: true,
                Message: "Connection successful",
                TestedAt: DateTimeOffset.UtcNow));
        }

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

        // Verify the namespace exists and is owned (not merely shared) by the caller first —
        // a shared collaborator must not be able to delete a namespace out from under its
        // owner. Subsequent operations use the repository-verified entity ID, not the raw
        // route value.
        var getResult = await GetExclusivelyOwnedNamespaceAsync(_namespaceRepository, id, cancellationToken);
        if (getResult.IsFailure)
            return ToActionResult(Result.Failure(getResult.Error));

        var ns = getResult.Value;

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

        // AWS/GCP client factories are only registered when their provider flag is enabled,
        // so they're resolved optionally here rather than as a hard constructor dependency.
        HttpContext.RequestServices.GetService<IAwsClientFactory>()?.RemoveClient(ns.Id);
        var gcpClientFactory = HttpContext.RequestServices.GetService<IGcpClientFactory>();
        if (gcpClientFactory is not null)
            await gcpClientFactory.RemoveClientAsync(ns.Id, cancellationToken);

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

        // Publish after commit — only fires when DeleteAsync has durably succeeded.
        if (_eventBus is not null)
        {
            var payload = new NamespaceDeletedPayload
            {
                NamespaceId = ns.Id,
                NamespaceName = ns.Name,
                CloudProvider = ns.Provider.ToString().ToLowerInvariant(),
                OwnerId = ns.OwnerId,
            };

            var evt = new PlatformEvent
            {
                Source = "ServiceHub.Api.Controllers.V1.NamespacesController",
                Category = EventCategories.Namespace,
                EventType = EventTypes.NamespaceDeleted,
                Severity = EventSeverity.Info,
                CloudProvider = ns.Provider.ToString().ToLowerInvariant(),
                NamespaceId = ns.Id,
                NamespaceName = ns.Name,
                CorrelationId = CorrelationId,
                Actor = OwnerId,
                TargetScope = ns.Name,
                Payload = payload,
            };

            await _eventBus.PublishAsync(evt, cancellationToken);

            _logger.LogDebug(
                "Published Platform Event {EventType} for NamespaceId {NamespaceId} CorrelationId {CorrelationId}",
                evt.EventType, ns.Id, evt.CorrelationId);
        }

        return NoContent();
    }

    /// <summary>
    /// Grants another owner identity live operational access to this namespace — browse
    /// entities, peek/replay/purge messages, Live Tail. Only the namespace's true owner may
    /// share it; a shared collaborator cannot re-share.
    /// </summary>
    /// <remarks>
    /// <b>Preview:</b> shared access covers live operations only. DLQ Intelligence history,
    /// Bulk Operation job history, and the audit trail remain scoped to whichever owner
    /// performed each action, and are not yet resolved through namespace sharing.
    /// </remarks>
    /// <param name="id">The namespace ID.</param>
    /// <param name="request">The owner ID to grant access to.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="200">Access granted; returns the updated namespace.</response>
    /// <response code="400">Invalid owner ID (e.g. sharing with the namespace's own owner, or the shared-owner cap was reached).</response>
    /// <response code="404">Namespace not found or not owned by the caller.</response>
    /// <response code="428">Missing explicit-intent headers.</response>
    [HttpPost("{id:guid}/share")]
    [RequireScope(ApiKeyScopes.NamespacesWrite)]
    [ProducesResponseType(typeof(NamespaceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<NamespaceResponse>> Share(
        [FromRoute] Guid id,
        [FromBody] ShareNamespaceRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IntentHeaders.HasExplicitIntent(HttpContext, IntentHeaders.IntentShareNamespace))
        {
            _auditLogger.LogCriticalAction(
                HttpContext, OwnerId, action: IntentHeaders.IntentShareNamespace, outcome: "Denied",
                namespaceId: id, detail: "Missing explicit intent headers");

            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Explicit Intent Required",
                detail: IntentHeaders.BuildIntentRequiredDetail("sharing a namespace"));
        }

        var getResult = await GetExclusivelyOwnedNamespaceAsync(_namespaceRepository, id, cancellationToken);
        if (getResult.IsFailure)
            return ToActionResult<NamespaceResponse>(getResult.Error);

        var ns = getResult.Value;

        var shareResult = ns.ShareWith(request.OwnerId);
        if (shareResult.IsFailure)
        {
            _auditLogger.LogCriticalAction(
                HttpContext, OwnerId, action: IntentHeaders.IntentShareNamespace, outcome: "Failed",
                namespaceId: ns.Id, environment: ns.Environment, resourceName: ns.Name,
                detail: shareResult.Error.Message);
            return ToActionResult<NamespaceResponse>(shareResult.Error);
        }

        var updateResult = await _namespaceRepository.UpdateAsync(ns, cancellationToken);
        if (updateResult.IsFailure)
        {
            _auditLogger.LogCriticalAction(
                HttpContext, OwnerId, action: IntentHeaders.IntentShareNamespace, outcome: "Failed",
                namespaceId: ns.Id, environment: ns.Environment, resourceName: ns.Name,
                detail: updateResult.Error.Message);
            return ToActionResult<NamespaceResponse>(updateResult.Error);
        }

        _auditLogger.LogCriticalAction(
            HttpContext, OwnerId, action: IntentHeaders.IntentShareNamespace, outcome: "Succeeded",
            namespaceId: ns.Id, environment: ns.Environment, resourceName: ns.Name,
            detail: $"Shared with owner {request.OwnerId}");

        _logger.LogInformation("Namespace {NamespaceId} shared with a new owner", ns.Id);

        return Ok(MapToResponse(ns));
    }

    /// <summary>
    /// Revokes a previously-granted owner's shared access to this namespace. Only the
    /// namespace's true owner may revoke access. Idempotent — revoking access an owner never
    /// had returns success.
    /// </summary>
    /// <param name="id">The namespace ID.</param>
    /// <param name="ownerId">The owner ID to revoke access from.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <response code="204">Access revoked (or the owner never had access).</response>
    /// <response code="404">Namespace not found or not owned by the caller.</response>
    /// <response code="428">Missing explicit-intent headers.</response>
    [HttpDelete("{id:guid}/share/{ownerId}")]
    [RequireScope(ApiKeyScopes.NamespacesWrite)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> RevokeShare(
        [FromRoute] Guid id,
        [FromRoute] string ownerId,
        CancellationToken cancellationToken = default)
    {
        if (!IntentHeaders.HasExplicitIntent(HttpContext, IntentHeaders.IntentShareNamespace))
        {
            _auditLogger.LogCriticalAction(
                HttpContext, OwnerId, action: IntentHeaders.IntentShareNamespace, outcome: "Denied",
                namespaceId: id, detail: "Missing explicit intent headers");

            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Explicit Intent Required",
                detail: IntentHeaders.BuildIntentRequiredDetail("revoking namespace access"));
        }

        var getResult = await GetExclusivelyOwnedNamespaceAsync(_namespaceRepository, id, cancellationToken);
        if (getResult.IsFailure)
            return ToActionResult(Result.Failure(getResult.Error));

        var ns = getResult.Value;
        ns.RevokeShare(ownerId);

        var updateResult = await _namespaceRepository.UpdateAsync(ns, cancellationToken);
        if (updateResult.IsFailure)
        {
            _auditLogger.LogCriticalAction(
                HttpContext, OwnerId, action: IntentHeaders.IntentShareNamespace, outcome: "Failed",
                namespaceId: ns.Id, environment: ns.Environment, resourceName: ns.Name,
                detail: updateResult.Error.Message);
            return ToActionResult(updateResult);
        }

        _auditLogger.LogCriticalAction(
            HttpContext, OwnerId, action: IntentHeaders.IntentShareNamespace, outcome: "Succeeded",
            namespaceId: ns.Id, environment: ns.Environment, resourceName: ns.Name,
            detail: $"Revoked access for owner {ownerId}");

        _logger.LogInformation("Namespace {NamespaceId} access revoked for an owner", ns.Id);

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
        var nsResult = await GetOwnedNamespaceAsync(_namespaceRepository, id, cancellationToken);
        if (nsResult.IsFailure)
            return ToActionResult<NamespaceStatsResponse>(nsResult.Error);

        var ns = nsResult.Value;

        // AWS/GCP: aggregate from the provider's own entity listing (already includes
        // active/dead-letter counts per entity) instead of the Azure-only client cache below.
        if (ns.Provider is CloudProviderType.Aws or CloudProviderType.Gcp)
        {
            var provider = _messagingProviders.FirstOrDefault(p => p.ProviderType == ns.Provider);
            if (provider is null)
                return Ok(new NamespaceStatsResponse(0, 0, 0, 0, 0, 0));

            var entitiesResult = await provider.ListEntitiesAsync(ns.Id, cancellationToken);
            if (entitiesResult.IsFailure)
                return Ok(new NamespaceStatsResponse(0, 0, 0, 0, 0, 0));

            var entities = entitiesResult.Value;
            var totalQueuesLive = entities.Count(e => string.Equals(e.EntityType, "Queue", StringComparison.OrdinalIgnoreCase));
            var totalTopicsLive = entities.Count(e => string.Equals(e.EntityType, "Topic", StringComparison.OrdinalIgnoreCase));
            var totalSubscriptionsLive = entities.Count(e => string.Equals(e.EntityType, "Subscription", StringComparison.OrdinalIgnoreCase));

            return Ok(new NamespaceStatsResponse(
                TotalQueues: totalQueuesLive,
                TotalTopics: totalTopicsLive,
                TotalSubscriptions: totalSubscriptionsLive,
                TotalActive: entities.Sum(e => e.ActiveMessageCount),
                TotalDlq: entities.Sum(e => e.DeadLetterCount),
                TotalScheduled: 0));
        }

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
    /// Maps a Namespace entity to a NamespaceResponse DTO, from the current caller's
    /// perspective (instance method, not static, so it can populate <see cref="NamespaceResponse.IsSharedWithMe"/>).
    /// </summary>
    /// <param name="ns">The namespace entity.</param>
    /// <returns>The namespace response.</returns>
    private NamespaceResponse MapToResponse(Namespace ns)
    {
        var isTrueOwner = string.Equals(ns.OwnerId, OwnerId, StringComparison.Ordinal);

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
            Environment: ns.Environment)
        {
            Provider = ns.Provider,
            AwsRegion = ns.AwsRegion,
            GcpProjectId = ns.GcpProjectId,
            // Only the true owner sees who else has access — a shared collaborator sees their
            // own access to the namespace, not the owner's full sharing list.
            SharedWithOwnerIds = isTrueOwner ? ns.SharedWithOwnerIds : [],
            IsSharedWithMe = !isTrueOwner,
        };
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
