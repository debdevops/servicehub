using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Bulk replay/purge operations against DLQ messages matching a filter — "replay these 3,000
/// messages matching this filter" as a durable, cancellable job with dry-run preview and
/// live progress, instead of a single request that can time out on large DLQs.
/// </summary>
[Route("api/v1/bulk-operations")]
[Tags("Bulk Operations")]
public sealed class BulkOperationsController : ApiControllerBase
{
    private readonly IBulkOperationService _bulkOperationService;
    private readonly IGovernanceAccessEvaluator _governanceAccessEvaluator;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<BulkOperationsController> _logger;

    /// <summary>Initialises a new instance of <see cref="BulkOperationsController"/>.</summary>
    public BulkOperationsController(
        IBulkOperationService bulkOperationService,
        IGovernanceAccessEvaluator governanceAccessEvaluator,
        IAuditLogger auditLogger,
        ILogger<BulkOperationsController> logger)
    {
        _bulkOperationService = bulkOperationService ?? throw new ArgumentNullException(nameof(bulkOperationService));
        _governanceAccessEvaluator = governanceAccessEvaluator ?? throw new ArgumentNullException(nameof(governanceAccessEvaluator));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Governance/RBAC check for bulk replay/purge — mirrors <c>RulesController</c>'s
    /// <c>EvaluateRuleGovernanceAsync</c>: the namespace lives in the request body
    /// (<c>request.Filter.NamespaceId</c>), not the route or query string, so the declarative
    /// <see cref="RequireGovernanceRoleAttribute"/> (which only reads route/query) can't resolve
    /// it — this endpoint is the same Recover-pillar mutation as
    /// <c>MessagesController.ReplayMessage</c>/<c>PurgeMessage</c>, just batched, so it is held to
    /// the same <see cref="GovernanceRole.Operator"/> bar.
    /// </summary>
    private async Task<Result> EvaluateBulkOperationGovernanceAsync(Guid namespaceId, CancellationToken cancellationToken)
    {
        var granteeIdentity = ResolveGovernanceGranteeIdentity();
        return await _governanceAccessEvaluator.EvaluateAsync(
            OwnerId, granteeIdentity, GovernanceRole.Operator, namespaceId, PillarKind.Recover, cancellationToken);
    }

    /// <summary>
    /// Dry-runs a bulk replay/purge: matches and samples messages without mutating anything.
    /// </summary>
    /// <param name="request">The operation type and message filter to preview.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("preview")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(BulkOperationPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BulkOperationPreviewResponse>> Preview(
        [FromBody] BulkOperationPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _bulkOperationService.PreviewAsync(OwnerId, request, AllowedNamespaceIds, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Creates and enqueues a bulk replay/purge job for every DLQ message matching the filter.
    /// Returns immediately with the job in <c>Pending</c> status — poll <c>GET /{id}</c> for
    /// progress.
    /// </summary>
    /// <param name="request">The operation type and message filter to execute against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="202">Job created and queued.</response>
    /// <response code="400">The filter matched no messages, or a safety guard rejected the request.</response>
    /// <response code="428">Missing explicit-intent headers — see <see cref="IntentHeaders"/>.</response>
    [HttpPost]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(BulkOperationJobResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<BulkOperationJobResponse>> Create(
        [FromBody] BulkOperationCreateRequest request,
        CancellationToken cancellationToken = default)
    {
        var governanceResult = await EvaluateBulkOperationGovernanceAsync(request.Filter.NamespaceId, cancellationToken);
        if (governanceResult.IsFailure)
        {
            return ToActionResult<BulkOperationJobResponse>(governanceResult.Error);
        }

        var expectedIntent = request.OperationType == Core.Enums.BulkOperationType.Purge
            ? IntentHeaders.IntentBulkPurge
            : IntentHeaders.IntentBulkReplay;

        if (!IntentHeaders.HasExplicitIntent(HttpContext, expectedIntent))
        {
            _auditLogger.LogCriticalAction(
                HttpContext, OwnerId, action: expectedIntent, outcome: "Denied",
                namespaceId: request.Filter.NamespaceId,
                detail: "Missing explicit intent headers");

            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Explicit Intent Required",
                detail: IntentHeaders.BuildIntentRequiredDetail($"bulk {request.OperationType.ToString().ToLowerInvariant()} operations"));
        }

        _auditLogger.LogCriticalAction(
            HttpContext, OwnerId, action: expectedIntent, outcome: "Attempt",
            namespaceId: request.Filter.NamespaceId,
            detail: $"entityFilter={request.Filter.EntityName}; statusFilter={request.Filter.Status}");

        var correlationId = HttpContext.Items["CorrelationId"]?.ToString();
        var result = await _bulkOperationService.CreateJobAsync(
            OwnerId, request, correlationId, ResolveRecoveryActor(), AllowedNamespaceIds, cancellationToken);

        if (result.IsFailure)
        {
            _auditLogger.LogCriticalAction(
                HttpContext, OwnerId, action: expectedIntent, outcome: "Denied",
                namespaceId: request.Filter.NamespaceId,
                detail: result.Error.Message);
            return ToActionResult(result);
        }

        _logger.LogInformation(
            "Bulk {OperationType} job {JobId} created by {OwnerId} for namespace {NamespaceId}",
            request.OperationType, result.Value.Id, OwnerId, request.Filter.NamespaceId);

        return AcceptedAtAction(nameof(Get), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>Gets a single bulk operation job's current status and progress.</summary>
    /// <param name="id">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(BulkOperationJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BulkOperationJobResponse>> Get(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _bulkOperationService.GetJobAsync(OwnerId, id, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Lists bulk operation jobs for the caller, most recent first.</summary>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (max 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(PaginatedResponse<BulkOperationJobResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginatedResponse<BulkOperationJobResponse>>> List(
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _bulkOperationService.ListJobsAsync(OwnerId, namespaceId, page, pageSize, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Requests cancellation of a pending or running job. Idempotent: cancelling an already
    /// finished job returns its current (terminal) state rather than an error. Held to the same
    /// <see cref="GovernanceRole.Operator"/> bar as <see cref="Create"/>: cancelling an in-flight
    /// Recover-pillar mutation is itself a governed action, not a free pass, the same way
    /// <c>RecoveryController</c>'s emergency-stop activate/clear both require a role rather than
    /// only the "start" direction being gated.
    /// </summary>
    /// <param name="id">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("{id:guid}/cancel")]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(BulkOperationJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BulkOperationJobResponse>> Cancel(
        Guid id, CancellationToken cancellationToken = default)
    {
        var jobResult = await _bulkOperationService.GetJobAsync(OwnerId, id, cancellationToken);
        if (jobResult.IsFailure)
        {
            return ToActionResult(jobResult);
        }

        var governanceResult = await EvaluateBulkOperationGovernanceAsync(jobResult.Value.NamespaceId, cancellationToken);
        if (governanceResult.IsFailure)
        {
            return ToActionResult<BulkOperationJobResponse>(governanceResult.Error);
        }

        var result = await _bulkOperationService.CancelJobAsync(OwnerId, id, cancellationToken);
        return ToActionResult(result);
    }
}
