using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Security;
using ServiceHub.Core.Constants;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Security;
using ServiceHub.Core.Validation;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Connect, test, list, inspect and remove a cloud namespace — the same six operations for every
/// cloud.
/// </summary>
/// <remarks>
/// <para>
/// <b>One entities endpoint, not three.</b> Which kinds a provider has is a capability fact, so
/// <c>GET …/entities?kind=</c> serves queues, topics and subscriptions alike and the UI never
/// needs a controller per kind.
/// </para>
/// <para>
/// <b>No provider is named here</b> (rule R4): dispatch goes through <see cref="ICloudProviderRouter"/>,
/// credential rules through <see cref="NamespaceCredentials"/>. And <b>no response carries a
/// connection string</b> — the type that would carry one does not exist.
/// </para>
/// </remarks>
[Route("api/v1/namespaces")]
public sealed class NamespacesController : ApiControllerBase
{
    private static readonly string[] EntityKinds = ["queue", "topic", "subscription"];

    private readonly INamespaceRepository _namespaces;
    private readonly IAuditTrail _audit;
    private readonly ICloudProviderRouter _router;
    private readonly ILogger<NamespacesController> _logger;

    /// <summary>Creates the controller.</summary>
    public NamespacesController(
        INamespaceRepository namespaces, IAuditTrail audit, ICloudProviderRouter router, ILogger<NamespacesController> logger)
    {
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Lists the namespaces this caller may see — and only those.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<NamespaceResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken cancellationToken)
    {
        var result = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        return result.IsFailure
            ? Problem(result.Error)
            : Ok(result.Value.OrderBy(n => n.CreatedAt).Select(ToResponse).ToList());
    }

    /// <summary>Gets one namespace.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NamespaceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Ok(ToResponse(result.Value));
    }

    /// <summary>Connects a namespace. Requires the <c>create-namespace</c> intent header.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(NamespaceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateNamespaceRequest request, CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.CreateNamespace))
        {
            return Problem(
                StatusCodes.Status428PreconditionRequired,
                ErrorCodes.IntentRequired,
                IntentHeaders.MissingDetail("connect this namespace", IntentHeaders.CreateNamespace));
        }

        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "connect a cloud", cancellationToken) is { } denied) return denied;

        _logger.LogInformation("Connecting namespace {Name}", LogRedactor.SanitiseForLog(request.Name));

        // Never store a namespace nothing in this build can serve.
        if (!_router.IsRegistered(request.Provider))
        {
            return Problem(
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.CapabilityUnavailable,
                $"This build of ServiceHub has no adapter for '{request.Provider}', so a namespace on it cannot be connected.");
        }

        var credentialCheck = NamespaceCredentials.Validate(request.Provider, request.AuthType, request.ConnectionString);
        if (credentialCheck.IsFailure)
        {
            return Problem(credentialCheck.Error);
        }

        // Duplicates are judged across the owner's whole pool, not only what this caller may see:
        // a restricted caller still gets a clean conflict rather than a name that collides with a
        // namespace it cannot see.
        var pool = await _namespaces.GetByOwnerAsync(OwnerId, allowedNamespaceIds: null, cancellationToken);
        if (pool.IsFailure)
        {
            return Problem(pool.Error);
        }

        var name = request.Name.Trim().ToLowerInvariant();
        if (pool.Value.Any(n => string.Equals(n.Name, name, StringComparison.Ordinal)))
        {
            return Problem(StatusCodes.Status409Conflict, ErrorCodes.Namespace.AlreadyExists,
                $"A namespace named '{request.Name}' is already connected.");
        }

        // The hash is taken from the plaintext so the same credential is recognised without ever
        // decrypting a stored one. The credential itself is encrypted by the database layer on save.
        var hash = Namespace.ComputeConnectionStringHash(request.ConnectionString);
        if (hash is not null && pool.Value.FirstOrDefault(n => n.ConnectionStringHash == hash) is { } duplicate)
        {
            return Problem(StatusCodes.Status409Conflict, ErrorCodes.Namespace.AlreadyExists,
                $"This credential is already connected as '{duplicate.DisplayName ?? duplicate.Name}'.");
        }

        var created = string.IsNullOrEmpty(request.ConnectionString)
            ? Namespace.CreateWithManagedIdentity(
                request.Name, request.AuthType, request.DisplayName, request.Description,
                request.Environment, request.Provider, OwnerId, request.AwsRegion, request.GcpProjectId)
            : Namespace.Create(
                request.Name, request.ConnectionString, request.DisplayName, request.Description,
                request.Environment, request.Provider, OwnerId, hash, request.AwsRegion, request.GcpProjectId);
        if (created.IsFailure)
        {
            return Problem(created.Error);
        }

        var saved = await _namespaces.AddAsync(created.Value, cancellationToken);
        if (saved.IsFailure)
        {
            await RecordAsync(AuditActions.NamespaceConnect, AuditActions.Failure, created.Value, saved.Error.Message, cancellationToken);
            return Problem(saved.Error);
        }

        await RecordAsync(AuditActions.NamespaceConnect, AuditActions.Success, created.Value, null, cancellationToken);

        _logger.LogInformation("Namespace {NamespaceId} connected", created.Value.Id);
        return CreatedAtAction(nameof(Get), new { id = created.Value.Id }, ToResponse(created.Value));
    }

    /// <summary>Probes the namespace live and records the outcome. A failed probe is a 200 that says so.</summary>
    [HttpPost("{id:guid}/test-connection")]
    [ProducesResponseType(typeof(ConnectionTestResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> TestConnection(Guid id, CancellationToken cancellationToken)
    {
        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        var ns = found.Value;
        if (!_router.IsRegistered(ns.Provider))
        {
            return NoAdapter(ns);
        }

        var probe = await _router.Resolve(ns.Provider).ValidateConnectionAsync(ns, cancellationToken);
        ns.RecordConnectionTest(probe.IsSuccess);

        var recorded = await _namespaces.UpdateAsync(ns, cancellationToken);
        if (recorded.IsFailure)
        {
            _logger.LogWarning("Could not record the connection test for {NamespaceId}: {Code}", id, recorded.Error.Code);
        }

        var message = probe.IsSuccess
            ? "Connection successful."
            : $"Connection test failed: {probe.Error.Message}";
        return Ok(new ConnectionTestResponse(probe.IsSuccess, message, DateTimeOffset.UtcNow));
    }

    /// <summary>Removes a namespace. Requires the <c>delete-namespace</c> intent header.</summary>
    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.DeleteNamespace))
        {
            return Problem(
                StatusCodes.Status428PreconditionRequired,
                ErrorCodes.IntentRequired,
                IntentHeaders.MissingDetail("remove this namespace", IntentHeaders.DeleteNamespace));
        }

        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        if (await DeniedUnlessAsync(GovernanceRole.Admin, id, null, "remove this namespace", cancellationToken) is { } denied) return denied;

        var deleted = await _namespaces.DeleteAsync(id, cancellationToken);
        if (deleted.IsFailure)
        {
            await RecordAsync(AuditActions.NamespaceRemove, AuditActions.Failure, found.Value, deleted.Error.Message, cancellationToken);
            return Problem(deleted.Error);
        }

        await RecordAsync(AuditActions.NamespaceRemove, AuditActions.Success, found.Value, null, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Looks at the namespace's dead letters now and records what it finds, so each one can be opened, read and
    /// replayed. This is how AWS and Google Cloud dead letters reach the list: ServiceHub never looks there on a
    /// timer, because a look is a receive that counts as a delivery attempt — a person asking is the consent.
    /// Requires the <c>look-at-dead-letters</c> intent header. A cloud that cannot be read is a 200 that says so.
    /// </summary>
    [HttpPost("{id:guid}/dead-letters/look")]
    [ProducesResponseType(typeof(DeadLetterLookResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> LookAtDeadLetters(Guid id, [FromServices] IDeadLetterLook look, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(look);
        if (!IntentHeaders.Declares(Request, IntentHeaders.LookAtDeadLetters))
        {
            return Problem(
                StatusCodes.Status428PreconditionRequired,
                ErrorCodes.IntentRequired,
                IntentHeaders.MissingDetail("look at these dead letters now", IntentHeaders.LookAtDeadLetters));
        }

        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        var ns = found.Value;
        if (!_router.IsRegistered(ns.Provider))
        {
            return NoAdapter(ns);
        }

        // A look is a delivery attempt on some clouds, so it is a recovery action, not a read.
        if (await DeniedUnlessAsync(GovernanceRole.Operator, ns.Id, PillarKind.Recover, "look at these dead letters", cancellationToken) is { } denied) return denied;

        var result = await look.LookNowAsync(ns, cancellationToken);
        if (result.Outcome == "busy")
        {
            return Problem(StatusCodes.Status409Conflict, ErrorCodes.AlreadyRunning, result.Reason ?? "Already looking.");
        }

        await RecordAsync(
            AuditActions.DeadLettersLook, result.Outcome == "looked" ? AuditActions.Success : AuditActions.Failure,
            ns, result.Reason, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// A namespace at a glance. Message totals are null — not zero — when the provider cannot count
    /// messages (rule R5).
    /// </summary>
    [HttpGet("{id:guid}/stats")]
    [ProducesResponseType(typeof(NamespaceStatsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Stats(Guid id, CancellationToken cancellationToken)
    {
        var listed = await ListEntitiesAsync(id, cancellationToken);
        if (listed.Failure is not null)
        {
            return listed.Failure;
        }

        var (ns, capabilities, entities) = listed.Value;
        var counts = capabilities.SupportsMessageCounts;

        // Messages live in queues and subscriptions; a topic only fans out to them.
        var holders = entities.Where(e => Kind(e) is "queue" or "subscription").ToList();

        return Ok(new NamespaceStatsResponse(
            ns.Id,
            EntityKinds
                .Select(k => new EntityKindCount(k, entities.Count(e => Kind(e) == k)))
                .Where(c => c.Count > 0)
                .ToList(),
            counts ? holders.Sum(e => e.ActiveMessageCount) : null,
            counts ? holders.Sum(e => e.DeadLetterCount) : null,
            counts,
            DateTimeOffset.UtcNow));
    }

    /// <summary>Lists a namespace's queues, topics and subscriptions, optionally of one <c>kind</c>.</summary>
    [HttpGet("{id:guid}/entities")]
    [ProducesResponseType(typeof(EntityListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Entities(Guid id, [FromQuery] string? kind, CancellationToken cancellationToken)
    {
        var wanted = kind?.Trim().ToLowerInvariant();
        if (wanted is not null && !EntityKinds.Contains(wanted))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed,
                $"'kind' must be one of: {string.Join(", ", EntityKinds)}.");
        }

        var listed = await ListEntitiesAsync(id, cancellationToken);
        if (listed.Failure is not null)
        {
            return listed.Failure;
        }

        var (_, capabilities, entities) = listed.Value;
        var counts = capabilities.SupportsMessageCounts;

        var items = entities
            .Where(e => wanted is null || Kind(e) == wanted)
            .OrderBy(e => Kind(e), StringComparer.Ordinal)
            .ThenBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => new EntityResponse(
                e.Name,
                Kind(e),
                counts ? e.ActiveMessageCount : null,
                counts ? e.DeadLetterCount : null,
                e.DeadLetterTargetName))
            .ToList();

        return Ok(new EntityListResponse(id, items));
    }

    private async Task<Listing> ListEntitiesAsync(Guid id, CancellationToken cancellationToken)
    {
        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return new Listing(default, Problem(found.Error));
        }

        var ns = found.Value;
        if (!_router.IsRegistered(ns.Provider))
        {
            return new Listing(default, NoAdapter(ns));
        }

        var provider = _router.Resolve(ns.Provider);
        var entities = await provider.ListEntitiesAsync(ns.Id, cancellationToken);
        return entities.IsFailure
            ? new Listing(default, Problem(entities.Error))
            : new Listing((ns, provider.Capabilities, entities.Value), null);
    }

    /// <summary>
    /// Writes one audit row. The row snapshots the namespace's name, provider and environment, so it
    /// still reads correctly after the namespace is gone. A failure to record is logged, never
    /// allowed to fail the request that has already happened.
    /// </summary>
    private async Task RecordAsync(string action, string outcome, Namespace ns, string? error, CancellationToken cancellationToken)
    {
        try
        {
            await _audit.RecordAsync(
                new AuditLog
                {
                    Id = Guid.NewGuid(),
                    Timestamp = DateTimeOffset.UtcNow,
                    OwnerId = OwnerId,
                    UserIdentity = Actor.Identity,
                    Action = action,
                    Outcome = outcome,
                    NamespaceId = ns.Id,
                    NamespaceName = ns.Name,
                    CloudProvider = ns.Provider.ToString().ToLowerInvariant(),
                    Environment = ns.Environment.ToString(),
                    ResourceName = ns.Name,
                    ErrorDetails = error is null ? null : LogRedactor.SanitiseForLog(error),
                    CorrelationId = HttpContext.TraceIdentifier,
                    HttpMethod = Request.Method,
                    HttpPath = Request.Path.Value,
                },
                cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not record {Action} for namespace {NamespaceId} in the audit trail", action, ns.Id);
        }
    }

    private ObjectResult NoAdapter(Namespace ns) =>
        Problem(
            StatusCodes.Status503ServiceUnavailable,
            ErrorCodes.CapabilityUnavailable,
            $"This build of ServiceHub has no adapter for '{ns.Provider}', so '{ns.Name}' cannot be reached.");

    /// <summary>
    /// The contract's three words. An adapter may describe an entity in its own service's terms ("sns topic"); the API
    /// answers in queue, topic or subscription — a screen indexes its wording by these and must never meet a fourth.
    /// </summary>
    internal static string Kind(CloudEntity entity)
    {
        var raw = entity.EntityType.Trim().ToLowerInvariant();
        return raw.Contains("subscription", StringComparison.Ordinal) ? "subscription"
            : raw.Contains("topic", StringComparison.Ordinal) ? "topic"
            : raw.Contains("queue", StringComparison.Ordinal) ? "queue"
            : raw;
    }

    private NamespaceResponse ToResponse(Namespace ns) => new(
        ns.Id,
        ns.Name,
        ns.DisplayName,
        ns.Description,
        ns.Provider,
        ns.Environment,
        ns.AuthType,
        ns.AwsRegion,
        ns.GcpProjectId,
        ns.IsActive,
        ns.CreatedAt,
        ns.LastConnectionTestAt,
        ns.LastConnectionTestSucceeded,
        _router.IsRegistered(ns.Provider) ? _router.Resolve(ns.Provider).Capabilities : null);

    private readonly record struct Listing((Namespace Ns, ProviderCapabilities Capabilities, IReadOnlyList<CloudEntity> Entities)? Success, ObjectResult? Failure)
    {
        public (Namespace Ns, ProviderCapabilities Capabilities, IReadOnlyList<CloudEntity> Entities) Value => Success!.Value;
    }
}
