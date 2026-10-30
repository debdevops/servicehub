using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Security;
using ServiceHub.Core.Components;
using ServiceHub.Core.Constants;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Security;
using ServiceHub.Core.Validation;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Signatures;

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
    private readonly IComponentSourceRouter _sources;
    private readonly ILogger<NamespacesController> _logger;

    /// <summary>Creates the controller.</summary>
    public NamespacesController(
        INamespaceRepository namespaces, IAuditTrail audit, ICloudProviderRouter router, IComponentSourceRouter sources, ILogger<NamespacesController> logger)
    {
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
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

        created.Value.DeclareCanSend(request.CanSend);
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

    /// <summary>The body of <c>PUT .../can-send</c>.</summary>
    /// <param name="CanSend">False is read-only; true is read and send; null clears the declaration (treated as may send).</param>
    public sealed record DeclareCanSendRequest(bool? CanSend);

    /// <summary>
    /// Says whether ServiceHub may send through this connection (C3). A cloud cannot always be asked what a credential may do, so
    /// the person declares it; a read-only connection refuses replay and manual Send. Requires the <c>declare-can-send</c> intent
    /// header and the Admin role for this namespace.
    /// </summary>
    [HttpPut("{id:guid}/can-send")]
    [ProducesResponseType(typeof(NamespaceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> DeclareCanSend(Guid id, [FromBody] DeclareCanSendRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IntentHeaders.Declares(Request, IntentHeaders.DeclareCanSend))
        {
            return Problem(
                StatusCodes.Status428PreconditionRequired,
                ErrorCodes.IntentRequired,
                IntentHeaders.MissingDetail("say whether this connection may send", IntentHeaders.DeclareCanSend));
        }

        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        var ns = found.Value;
        if (await DeniedUnlessAsync(GovernanceRole.Admin, id, null, "say whether this connection may send", cancellationToken) is { } denied) return denied;

        ns.DeclareCanSend(request.CanSend);
        var saved = await _namespaces.UpdateAsync(ns, cancellationToken);
        if (saved.IsFailure)
        {
            await RecordAsync(AuditActions.NamespaceDeclareCanSend, AuditActions.Failure, ns, saved.Error.Message, cancellationToken);
            return Problem(saved.Error);
        }

        await RecordAsync(AuditActions.NamespaceDeclareCanSend, AuditActions.Success, ns, null, cancellationToken);
        return Ok(ToResponse(ns));
    }

    /// <summary>The body of <c>PUT .../owner-channel</c>.</summary>
    /// <param name="ChannelId">The notification channel to tell about this connection; null means every enabled channel.</param>
    public sealed record SetOwnerChannelRequest(Guid? ChannelId);

    /// <summary>
    /// Chooses the one notification channel that is told about this connection (G7). Needs the <c>set-owner-channel</c> intent
    /// header and the Admin role for this namespace; the channel must be one of this owner's.
    /// </summary>
    [HttpPut("{id:guid}/owner-channel")]
    [ProducesResponseType(typeof(NamespaceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> SetOwnerChannel(
        Guid id, [FromBody] SetOwnerChannelRequest request, [FromServices] ServiceHubDbContext db, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(db);
        if (!IntentHeaders.Declares(Request, IntentHeaders.SetOwnerChannel))
        {
            return Problem(
                StatusCodes.Status428PreconditionRequired,
                ErrorCodes.IntentRequired,
                IntentHeaders.MissingDetail("choose who is told about this connection", IntentHeaders.SetOwnerChannel));
        }

        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        var ns = found.Value;
        if (await DeniedUnlessAsync(GovernanceRole.Admin, id, null, "choose who is told about this connection", cancellationToken) is { } denied) return denied;

        if (request.ChannelId is { } channelId
            && !await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AnyAsync(
                db.NotificationChannels, c => c.Id == channelId && c.OwnerId == OwnerId, cancellationToken))
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "That notification channel was not found.");
        }

        ns.AssignOwnerChannel(request.ChannelId);
        var saved = await _namespaces.UpdateAsync(ns, cancellationToken);
        if (saved.IsFailure)
        {
            await RecordAsync(AuditActions.NamespaceSetOwnerChannel, AuditActions.Failure, ns, saved.Error.Message, cancellationToken);
            return Problem(saved.Error);
        }

        await RecordAsync(AuditActions.NamespaceSetOwnerChannel, AuditActions.Success, ns, null, cancellationToken);
        return Ok(ToResponse(ns));
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

        try
        {
            var recorded = await _namespaces.UpdateAsync(ns, cancellationToken);
            if (recorded.IsFailure)
            {
                _logger.LogWarning("Could not record the connection test for {NamespaceId}: {Code}", id, recorded.Error.Code);
            }
        }
        catch (InvalidOperationException ex)
        {
            // The database layer refuses to save a namespace whose stored credential it cannot re-protect — the encryption
            // key it was saved under is no longer configured. Nothing is written; the probe's answer is still the answer.
            _logger.LogWarning(ex, "Could not record the connection test for {NamespaceId}: its stored credential cannot be re-protected", id);
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
    /// Where this cloud's DLQ observer stands (unit 4.2): whether it is needed, whether a person turned it on, and whether its own
    /// log has shown a test message recently. "Live" is never assumed.
    /// </summary>
    [HttpGet("{id:guid}/dlq-observer")]
    [ProducesResponseType(typeof(DlqObserverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDlqObserver(
        Guid id, [FromServices] IDlqObserverAttestationService attestations, [FromServices] IEnumerable<IDeadLetterReturnCheck> checks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attestations);
        ArgumentNullException.ThrowIfNull(checks);
        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        var ns = found.Value;
        var needed = _router.IsRegistered(ns.Provider) && !_router.Resolve(ns.Provider).Capabilities.CanProveDlqAbsence;
        return Ok(ToObserverResponse(needed, await attestations.GetAsync(OwnerId, id, cancellationToken), checks.FirstOrDefault(c => c.Provider == ns.Provider)));
    }

    /// <summary>
    /// Switches on, for a cloud that cannot confirm a fix by itself, ServiceHub's way of seeing that cloud's whole dead-letter queue
    /// (ADR-0018). What has to be named depends on the cloud's own check: nothing where ServiceHub reads the queue itself, a
    /// subscription of ServiceHub's own where it reads one. It confirms nothing until the Fix Confirmer agent has seen it work. Requires the <c>configure-dlq-observer</c> intent header
    /// and the Admin role for this namespace. A cloud that can confirm a fix on its own (Azure) refuses: there is nothing to set up.
    /// </summary>
    [HttpPut("{id:guid}/dlq-observer")]
    [ProducesResponseType(typeof(DlqObserverResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ConfigureDlqObserver(
        Guid id, [FromBody] ConfigureDlqObserverRequest request, [FromServices] IDlqObserverAttestationService attestations,
        [FromServices] IEnumerable<IDeadLetterReturnCheck> checks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(attestations);
        ArgumentNullException.ThrowIfNull(checks);
        if (!IntentHeaders.Declares(Request, IntentHeaders.ConfigureDlqObserver))
        {
            return Problem(
                StatusCodes.Status428PreconditionRequired,
                ErrorCodes.IntentRequired,
                IntentHeaders.MissingDetail("change this cloud's DLQ observer", IntentHeaders.ConfigureDlqObserver));
        }

        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        var ns = found.Value;
        if (await DeniedUnlessAsync(GovernanceRole.Admin, id, null, "change this cloud's DLQ observer", cancellationToken) is { } denied) return denied;

        if (!_router.IsRegistered(ns.Provider))
        {
            return NoAdapter(ns);
        }

        if (_router.Resolve(ns.Provider).Capabilities.CanProveDlqAbsence)
        {
            return Problem(
                StatusCodes.Status409Conflict,
                ErrorCodes.CapabilityUnavailable,
                $"{ns.Provider} can confirm a replay stayed fixed on its own, so there is no DLQ observer to set up for it.");
        }

        var staleness = request.StalenessBoundMinutes ?? 30;
        if (staleness is < 3 or > 1440)
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationFailed,
                "The longest an observer may go without a confirmed test message must be between 3 minutes and 24 hours.");
        }

        var observerReference = request.ObserverReference?.Trim();
        var entityName = request.DlqEntityName?.Trim();
        // ADR-0018: where this cloud has a way to see its whole dead-letter queue, that way says what it needs to be told.
        // Asked of the cloud's own check — never decided here by the cloud's name.
        if (request.Enabled && checks.FirstOrDefault(c => c.Provider == ns.Provider) is { } check)
        {
            if (!check.NeedsObserverReference)
            {
                observerReference = "whole-queue-scan"; // nothing is deployed for it: ServiceHub reads the queue itself
                entityName = string.IsNullOrEmpty(entityName) ? "*" : entityName;
            }
            else if (check.ValidateObserverReference(observerReference) is { } why)
            {
                return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, why);
            }
        }

        if (request.Enabled && (string.IsNullOrEmpty(observerReference) || string.IsNullOrEmpty(entityName)))
        {
            return Problem(
                StatusCodes.Status400BadRequest,
                ErrorCodes.ValidationFailed,
                "To turn the observer on, say where it writes (its table or collection) and which dead-letter queue the test message goes to.");
        }

        var saved = await attestations.ConfigureAsync(OwnerId, id, request.Enabled, observerReference, entityName, staleness, cancellationToken);
        if (saved.IsFailure)
        {
            await RecordAsync(AuditActions.DlqObserverConfigure, AuditActions.Failure, ns, saved.Error.Message, cancellationToken);
            return Problem(saved.Error);
        }

        await RecordAsync(AuditActions.DlqObserverConfigure, AuditActions.Success, ns, null, cancellationToken);
        return Ok(ToObserverResponse(true, saved.Value, checks.FirstOrDefault(c => c.Provider == ns.Provider)));
    }

    /// <summary>
    /// Regroups a namespace's dead letters by error message (design 10). <c>dryRun</c> (the default) writes nothing and says what would
    /// change. A real run is one transaction, safe to repeat, and switches off rules whose signature was split. Requires the
    /// <c>resign-signatures</c> intent header and the Admin role for this namespace.
    /// </summary>
    [HttpPost("{id:guid}/signatures/resign")]
    [ProducesResponseType(typeof(ResignResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> ResignSignatures(
        Guid id, [FromServices] ServiceHubDbContext db, CancellationToken cancellationToken, [FromQuery] bool dryRun = true)
    {
        ArgumentNullException.ThrowIfNull(db);
        if (!IntentHeaders.Declares(Request, IntentHeaders.ResignSignatures))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("regroup failures by error message", IntentHeaders.ResignSignatures));
        }

        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        if (await DeniedUnlessAsync(GovernanceRole.Admin, id, null, "regroup failures by error message", cancellationToken) is { } denied) return denied;

        var result = await SignatureResigner.ResignNamespaceAsync(db, OwnerId, id, dryRun, ct: cancellationToken);
        _logger.LogInformation("Re-sign of namespace {NamespaceId} (dryRun={DryRun}): {Changed} of {Messages} changed", id, dryRun, result.MessagesChanged, result.Messages);
        return Ok(result);
    }

    /// <summary>
    /// Checks now whether ServiceHub can see this cloud's whole dead-letter queue (ADR-0018). With <c>entity</c>, and where the
    /// cloud's check can, it also looks at that one queue and says whether it saw all of it. Changes nothing in the cloud.
    /// Requires the <c>configure-dlq-observer</c> intent header and the Admin role for this namespace.
    /// </summary>
    [HttpPost("{id:guid}/dlq-observer/check")]
    [ProducesResponseType(typeof(DlqObserverCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> CheckDlqObserver(
        Guid id, [FromQuery] string? entity, [FromServices] IDlqObserverAttestationService attestations,
        [FromServices] IEnumerable<IDeadLetterReturnCheck> checks, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attestations);
        ArgumentNullException.ThrowIfNull(checks);
        if (!IntentHeaders.Declares(Request, IntentHeaders.ConfigureDlqObserver))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("check this cloud's dead-letter view", IntentHeaders.ConfigureDlqObserver));
        }

        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        var ns = found.Value;
        if (await DeniedUnlessAsync(GovernanceRole.Admin, id, null, "check this cloud's dead-letter view", cancellationToken) is { } denied) return denied;

        var check = checks.FirstOrDefault(c => c.Provider == ns.Provider);
        var attestation = await attestations.GetAsync(OwnerId, id, cancellationToken);
        if (check is null || attestation is null)
        {
            return Problem(StatusCodes.Status409Conflict, ErrorCodes.CapabilityUnavailable, "Nothing is set up to check for this cloud yet.");
        }

        var health = await check.CheckHealthAsync(ns, attestation, cancellationToken);
        var probe = !string.IsNullOrWhiteSpace(entity) && check is IDeadLetterWholeViewProbe prober
            ? await prober.ProbeAsync(ns, entity.Trim(), cancellationToken)
            : null;
        return Ok(new DlqObserverCheckResponse(health.Healthy, health.Reason, probe?.Complete, probe?.Reason, probe?.Count));
    }

    /// <summary>What "check now" found.</summary>
    /// <param name="Healthy">Whether the view is usable right now.</param>
    /// <param name="Reason">Why not.</param>
    /// <param name="Complete">For one queue: whether all of it was seen. Null when no queue was asked about.</param>
    /// <param name="IncompleteReason">Why it was not all seen.</param>
    /// <param name="Count">How many messages were seen in that queue.</param>
    public sealed record DlqObserverCheckResponse(bool Healthy, string? Reason, bool? Complete, string? IncompleteReason, int? Count);

    private static DlqObserverResponse ToObserverResponse(bool needed, DlqObserverAttestation? a, IDeadLetterReturnCheck? check = null)
    {
        var needsReference = needed && check is { NeedsObserverReference: true };
        var hint = needsReference ? check!.ValidateObserverReference(null) : null;
        return Describe(needed, a) with { NeedsReference = needsReference, ReferenceHint = hint };
    }

    private static DlqObserverResponse Describe(bool needed, DlqObserverAttestation? a)
    {
        if (!needed)
        {
            return new DlqObserverResponse(false, false, false, null, null, 30, null, null,
                "This cloud can confirm a replay stayed fixed on its own — no observer needed.");
        }

        if (a is not { Enabled: true })
        {
            return new DlqObserverResponse(true, false, false, a?.ObserverReference, a?.DlqEntityName, a?.StalenessBoundMinutes ?? 30,
                a?.LastCanarySentAt, a?.LastConfirmedAt,
                "No observer is set up, so a replay here can be sent back but not confirmed as fixed.");
        }

        var live = a.IsLiveAt(DateTimeOffset.UtcNow);
        var status = live
            ? "ServiceHub could see this cloud's dead letters when it last checked, so a replay here can be confirmed."
            : a.LastConfirmedAt is null
                ? "Turned on, but ServiceHub has not yet been able to see this cloud's dead letters — not confirming anything."
                : "Turned on, but ServiceHub has not been able to see this cloud's dead letters lately — not confirming anything until it can.";
        return new DlqObserverResponse(true, true, live, a.ObserverReference, a.DlqEntityName, a.StalenessBoundMinutes, a.LastCanarySentAt, a.LastConfirmedAt, status);
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

        // Messages live in queues and subscriptions; a topic only fans out to them. A queue that another entity names as its
        // dead-letter target (an SQS DLQ is an ordinary queue) holds dead letters: its messages are already counted as the source's
        // dead letters, so counting them again as active would show every dead letter twice.
        var deadLetterTargets = entities
            .Select(e => e.DeadLetterTargetName)
            .Where(n => !string.IsNullOrEmpty(n))
            .ToHashSet(StringComparer.Ordinal);
        var holders = entities
            .Where(e => Kind(e) is "queue" or "subscription" && !deadLetterTargets.Contains(e.Name))
            .ToList();

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

    /// <summary>
    /// Reads one entity's limits and settings (lock or visibility timeout, time to live, maximum deliveries) from the cloud.
    /// <c>settings</c> is null when the cloud does not read them for this kind of entity: "not read", never an empty list.
    /// </summary>
    [HttpGet("{id:guid}/entities/settings")]
    [ProducesResponseType(typeof(EntitySettingsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EntitySettings(Guid id, [FromQuery] string? name, [FromQuery] string? kind, CancellationToken cancellationToken)
    {
        var wanted = kind?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(name) || wanted is null || !EntityKinds.Contains(wanted))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed,
                $"'name' is required and 'kind' must be one of: {string.Join(", ", EntityKinds)}.");
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

        var read = await _router.Resolve(ns.Provider).GetEntitySettingsAsync(ns.Id, name.Trim(), wanted, cancellationToken);
        if (read.IsFailure)
        {
            return Problem(read.Error);
        }

        return Ok(new EntitySettingsResponse(
            name.Trim(),
            wanted,
            read.Value?.Select(s => new EntitySettingResponse(s.Label, s.Value)).ToList()));
    }

    /// <summary>
    /// Whether this connection can read a component that is not a message store (failed runs, failed deliveries), and its places.
    /// Read-only. When the credential cannot read it, <c>available</c> is false and <c>reason</c> says why.
    /// </summary>
    [HttpGet("{id:guid}/components/{componentKey}/source")]
    [ProducesResponseType(typeof(ComponentSourceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ComponentSource(Guid id, string componentKey, CancellationToken cancellationToken)
    {
        var resolved = await ResolveSourceAsync(id, componentKey, cancellationToken);
        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        var (ns, source) = (resolved.Namespace!, resolved.Source!);
        var access = await source.ValidateAccessAsync(ns, cancellationToken);
        if (access.IsFailure)
        {
            return Ok(new ComponentSourceResponse(source.ComponentKey, false, access.Error.Message, source.Capabilities.ReasonInList, []));
        }

        var places = await source.ListPlacesAsync(ns, cancellationToken);
        return places.IsFailure
            ? Ok(new ComponentSourceResponse(source.ComponentKey, false, places.Error.Message, source.Capabilities.ReasonInList, []))
            : Ok(new ComponentSourceResponse(
                source.ComponentKey, true, null, source.Capabilities.ReasonInList,
                [.. places.Value.Items.Select(p => new SourcePlaceResponse(p.Id, p.Name, p.Kind, p.ConsoleUrl))],
                places.Value.ReachedLimit, places.Value.Incomplete));
    }

    // ── Add a component (5.0.0): what can be brought in on a connection, a read-only access check, and ticking ─────────────────────
    // The listing is a read under components/. Everything that changes a choice lives under enabled-components/, so the rule that every
    // components/ route is a GET (ComponentModuleTests) stays true.

    /// <summary>
    /// The components the Add a component dialog lists for this connection: what each is, how it is reached (the connection already
    /// there, or its own credential), its prerequisites, and whether it is on. Read-only.
    /// </summary>
    [HttpGet("{id:guid}/components")]
    [ProducesResponseType(typeof(ComponentListResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Components(Guid id, [FromServices] INamespaceComponentService stored, CancellationToken cancellationToken)
    {
        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        var ns = found.Value;
        if (ComponentCatalog.CloudOf(ns.Provider) is not { } cloud)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "No components are listed for this connection's cloud.");
        }

        var choices = (await stored.ListAsync(OwnerId, id, cancellationToken)).ToDictionary(c => c.ComponentKey, StringComparer.Ordinal);
        var entries = ComponentAccessRules.Listed(cloud).Select(row => ToEntry(row, ns, choices)).ToList();
        return Ok(new ComponentListResponse(ns.Id, ns.DisplayName ?? ns.Name, ComponentCatalog.CloudName(cloud), entries));
    }

    /// <summary>
    /// Check access: asks the cloud, with list and describe calls only, whether this connection's credential may read the component.
    /// Nothing is saved and nothing in the cloud is changed. A "no" is a state with the reason, not an error.
    /// </summary>
    [HttpPost("{id:guid}/enabled-components/{componentKey}/check")]
    [ProducesResponseType(typeof(ComponentCheckResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CheckComponent(Guid id, string componentKey, CancellationToken cancellationToken)
    {
        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        var permissions = ComponentAccessRules.SetupFor(componentKey)?.Permissions ?? [];
        var source = _sources.Find(componentKey, found.Value.Provider);
        if (source is null)
        {
            return Ok(new ComponentCheckResponse(componentKey, false, "No access check is written for this component yet.", permissions));
        }

        var access = await source.ValidateAccessAsync(found.Value, cancellationToken);
        return Ok(new ComponentCheckResponse(componentKey, access.IsSuccess, access.IsSuccess ? null : access.Error.Message, permissions));
    }

    /// <summary>
    /// Ticks or unticks one component on this connection. Ticking checks access first and saves nothing if the check fails; unticking
    /// keeps every record the component produced. Requires the <c>manage-components</c> intent header and the Admin role.
    /// </summary>
    [HttpPut("{id:guid}/enabled-components/{componentKey}")]
    [ProducesResponseType(typeof(ComponentEntryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> SetComponent(
        Guid id, string componentKey, [FromBody] SetComponentRequest request, [FromServices] INamespaceComponentService stored, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IntentHeaders.Declares(Request, IntentHeaders.ManageComponents))
        {
            return Problem(
                StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired,
                IntentHeaders.MissingDetail("add or remove a component", IntentHeaders.ManageComponents));
        }

        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return Problem(found.Error);
        }

        var ns = found.Value;
        if (await DeniedUnlessAsync(GovernanceRole.Admin, id, null, "add or remove a component", cancellationToken) is { } denied) return denied;

        var cloud = ComponentCatalog.CloudOf(ns.Provider);
        var row = cloud is { } c ? ComponentAccessRules.Listed(c).FirstOrDefault(r => r.ComponentKey == componentKey) : null;
        if (row is null)
        {
            return Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"'{componentKey}' is not a component of this connection's cloud.");
        }

        // Only a component read on demand can be ticked here. A message store is watched on every connection (removing the connection
        // removes it), and a component nothing reads yet cannot be switched on.
        if (!ComponentAccessRules.ReadsOnDemand(row) || ComponentAccessRules.Of(row) is not (ComponentAccess.SameConnection or ComponentAccess.OwnCredential))
        {
            return Problem(StatusCodes.Status409Conflict, ErrorCodes.CapabilityUnavailable, $"{row.Name} cannot be ticked or unticked here.");
        }

        if (request.Enabled)
        {
            var source = _sources.Find(componentKey, ns.Provider);
            if (source is null)
            {
                return Problem(StatusCodes.Status409Conflict, ErrorCodes.CapabilityUnavailable, $"No reader is registered for {row.Name} on this connection.");
            }

            var access = await source.ValidateAccessAsync(ns, cancellationToken);
            if (access.IsFailure)
            {
                await RecordAsync(AuditActions.ComponentEnable, AuditActions.Failure, ns, $"{row.Name}: {access.Error.Message}", cancellationToken);
                return Problem(StatusCodes.Status409Conflict, ErrorCodes.CapabilityUnavailable, $"Not added. {access.Error.Message}");
            }
        }

        var saved = await stored.SetAsync(OwnerId, id, componentKey, request.Enabled, Actor.Identity, cancellationToken);
        if (saved.IsFailure)
        {
            await RecordAsync(request.Enabled ? AuditActions.ComponentEnable : AuditActions.ComponentDisable, AuditActions.Failure, ns, saved.Error.Message, cancellationToken);
            return Problem(saved.Error);
        }

        await RecordAsync(request.Enabled ? AuditActions.ComponentEnable : AuditActions.ComponentDisable, AuditActions.Success, ns, row.Name, cancellationToken);
        return Ok(ToEntry(row, ns, new Dictionary<string, NamespaceComponent>(StringComparer.Ordinal) { [componentKey] = saved.Value }));
    }

    /// <summary>Every component ticked on any connection, for the menu: a read-on-demand component is listed only where someone turned it on.</summary>
    [HttpGet("components/enabled")]
    [ProducesResponseType(typeof(IReadOnlyList<EnabledComponentResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> EnabledComponents([FromServices] INamespaceComponentService stored, CancellationToken cancellationToken)
    {
        var all = await stored.ListAllAsync(OwnerId, cancellationToken);
        var namespaces = await _namespaces.GetAllAsync(cancellationToken);
        if (namespaces.IsFailure)
        {
            return Problem(namespaces.Error);
        }

        var byId = namespaces.Value.ToDictionary(n => n.Id);
        var enabled = all
            .Where(c => c.Enabled && byId.ContainsKey(c.NamespaceId))
            .Select(c => (Row: c, Cloud: ComponentCatalog.CloudOf(byId[c.NamespaceId].Provider)))
            .Where(x => x.Cloud is not null)
            .Select(x => new EnabledComponentResponse(x.Row.NamespaceId, Camel(x.Cloud!.Value.ToString()), x.Row.ComponentKey))
            .ToList();
        return Ok(enabled);
    }

    private static string Camel(string word) => char.ToLowerInvariant(word[0]) + word[1..];

    private static ComponentEntryResponse ToEntry(ComponentDescriptor row, Namespace ns, IReadOnlyDictionary<string, NamespaceComponent> choices)
    {
        var access = ComponentAccessRules.Of(row);
        var setup = ComponentAccessRules.SetupFor(row.ComponentKey);
        var alwaysOn = ComponentAccessRules.IsAlwaysOn(row);
        var toggleable = ComponentAccessRules.ReadsOnDemand(row) && access is ComponentAccess.SameConnection or ComponentAccess.OwnCredential;
        var state = access switch
        {
            ComponentAccess.Included => "included",
            ComponentAccess.NotBuildable => "unavailable",
            _ when alwaysOn => "alwaysOn",
            _ => choices.TryGetValue(row.ComponentKey, out var c) && c.Enabled ? "on" : "off",
        };
        return new ComponentEntryResponse(
            row.ComponentKey, row.Name, row.StuckPlace, Camel(row.Status.ToString()),
            ComponentCatalog.StatusWords(row.Status), Camel(access.ToString()), state, toggleable,
            row.Note, setup?.What, setup?.Appears, setup?.Needs, setup?.Steps ?? [], [.. (setup?.Snippets ?? []).Select(x => new SetupSnippetResponse(x.Label, x.Text))],
            setup?.Permissions ?? [], setup?.IncludedIn);
    }

    /// <summary>Failed runs or deliveries of a component that is not a message store, newest first and capped. Read-only.</summary>
    [HttpGet("{id:guid}/components/{componentKey}/failed")]
    [ProducesResponseType(typeof(FailedItemsResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ComponentFailed(Guid id, string componentKey, [FromQuery] string? place, CancellationToken cancellationToken)
    {
        var resolved = await ResolveSourceAsync(id, componentKey, cancellationToken);
        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        var read = await resolved.Source!.ListFailedAsync(resolved.Namespace!, string.IsNullOrWhiteSpace(place) ? null : place, cancellationToken);
        return read.IsFailure
            ? Problem(read.Error)
            : Ok(new FailedItemsResponse(resolved.Source.ComponentKey, [.. read.Value.Items.Select(ToResponse)], read.Value.ReachedLimit, read.Value.Incomplete));
    }

    /// <summary>One failed item in full, redacted. Read-only. The id is a query value because clouds use paths and colons in theirs.</summary>
    [HttpGet("{id:guid}/components/{componentKey}/failed/detail")]
    [ProducesResponseType(typeof(FailedItemDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ComponentFailedDetail(Guid id, string componentKey, [FromQuery] string? itemId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(itemId))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'itemId' is required.");
        }

        var resolved = await ResolveSourceAsync(id, componentKey, cancellationToken);
        if (resolved.Failure is not null)
        {
            return resolved.Failure;
        }

        var read = await resolved.Source!.GetFailedAsync(resolved.Namespace!, itemId.Trim(), cancellationToken);
        if (read.IsFailure)
        {
            return Problem(read.Error);
        }

        return Ok(read.Value is null
            ? new FailedItemDetailResponse(null, null, null, null, null)
            : new FailedItemDetailResponse(ToResponse(read.Value.Item), read.Value.Reason, read.Value.Cause, read.Value.StartedUtc, read.Value.StoppedUtc));
    }

    private static FailedItemResponse ToResponse(FailedItem i) =>
        new(i.Id, i.Place, i.AtUtc, i.Status, i.ReasonCode, i.Summary, i.Copy.ToString().ToLowerInvariant(), i.ConsoleUrl);

    private readonly record struct ResolvedSource(Namespace? Namespace, IComponentSource? Source, IActionResult? Failure);

    private async Task<ResolvedSource> ResolveSourceAsync(Guid id, string componentKey, CancellationToken cancellationToken)
    {
        var found = await GetVisibleNamespaceAsync(_namespaces, id, cancellationToken);
        if (found.IsFailure)
        {
            return new ResolvedSource(null, null, Problem(found.Error));
        }

        var source = _sources.Find(componentKey, found.Value.Provider);
        return source is null
            ? new ResolvedSource(null, null, Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"No reader is registered for '{componentKey}' on this connection."))
            : new ResolvedSource(found.Value, source, null);
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
    internal static string Kind(CloudEntity entity) => CloudEntityKinds.Of(entity);

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
        _router.IsRegistered(ns.Provider) ? _router.Resolve(ns.Provider).Capabilities : null,
        ns.CanSend,
        ns.OwnerChannelId);

    private readonly record struct Listing((Namespace Ns, ProviderCapabilities Capabilities, IReadOnlyList<CloudEntity> Entities)? Success, ObjectResult? Failure)
    {
        public (Namespace Ns, ProviderCapabilities Capabilities, IReadOnlyList<CloudEntity> Entities) Value => Success!.Value;
    }
}
