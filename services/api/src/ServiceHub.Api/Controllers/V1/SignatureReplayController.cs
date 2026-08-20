using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Filters;
using ServiceHub.Api.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Replays every DLQ message currently belonging to a failure signature — "replay this whole
/// recurring failure" as a dry-run-first, cancellable, progress-reporting, durable job, reusing
/// the same <see cref="IMessageOperationsService"/> replay call single-message and bulk replay
/// already go through. See <see cref="ISignatureReplayService"/> for why this has its own job
/// pipeline (signature-scoped) instead of reusing <c>BulkOperationsController</c>'s filter-scoped one.
/// </summary>
[Tags("Signature Replay")]
public sealed class SignatureReplayController : ApiControllerBase
{
    private readonly ISignatureReplayService _signatureReplayService;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<SignatureReplayController> _logger;

    /// <summary>Initialises a new instance of <see cref="SignatureReplayController"/>.</summary>
    public SignatureReplayController(
        ISignatureReplayService signatureReplayService,
        IAuditLogger auditLogger,
        ILogger<SignatureReplayController> logger)
    {
        _signatureReplayService = signatureReplayService ?? throw new ArgumentNullException(nameof(signatureReplayService));
        _auditLogger = auditLogger ?? throw new ArgumentNullException(nameof(auditLogger));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Dry-runs a signature replay: resolves the signature's current member messages, matches
    /// and samples them against the filter, without mutating anything.
    /// </summary>
    /// <param name="namespaceId">The namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's stable identity hash.</param>
    /// <param name="request">The filter to preview.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("~/" + ApiRoutes.Dlq.SignatureReplayPreview)]
    [RequireNamespaceOwnership]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(BulkOperationPreviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BulkOperationPreviewResponse>> Preview(
        Guid namespaceId,
        string signatureHash,
        [FromBody] SignatureReplayScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        var filter = new SignatureReplayFilterRequest(namespaceId, signatureHash, request.Status, request.From, request.To);
        var result = await _signatureReplayService.PreviewAsync(
            OwnerId, new SignatureReplayPreviewRequest(filter), cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Starts a replay of every DLQ message matching the signature and filter. Returns
    /// immediately with the job in <c>Pending</c> status — poll
    /// <c>GET /api/v1/signature-replay-jobs/{id}</c> for progress.
    /// </summary>
    /// <param name="namespaceId">The namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's stable identity hash.</param>
    /// <param name="request">The filter to execute against.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="202">Job created and started.</response>
    /// <response code="400">The filter matched no messages, or a safety guard rejected the request.</response>
    /// <response code="428">Missing explicit-intent headers — see <see cref="IntentHeaders"/>.</response>
    [HttpPost("~/" + ApiRoutes.Dlq.SignatureReplay)]
    [RequireNamespaceOwnership]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(BulkOperationJobResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<BulkOperationJobResponse>> Start(
        Guid namespaceId,
        string signatureHash,
        [FromBody] SignatureReplayScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IntentHeaders.HasExplicitIntent(HttpContext, IntentHeaders.IntentSignatureReplay))
        {
            _auditLogger.LogCriticalAction(
                HttpContext, OwnerId, action: IntentHeaders.IntentSignatureReplay, outcome: "Denied",
                namespaceId: namespaceId,
                detail: "Missing explicit intent headers");

            return Problem(
                statusCode: StatusCodes.Status428PreconditionRequired,
                title: "Explicit Intent Required",
                detail: IntentHeaders.BuildIntentRequiredDetail("signature replay"));
        }

        var filter = new SignatureReplayFilterRequest(namespaceId, signatureHash, request.Status, request.From, request.To);

        _auditLogger.LogCriticalAction(
            HttpContext, OwnerId, action: IntentHeaders.IntentSignatureReplay, outcome: "Attempt",
            namespaceId: namespaceId,
            detail: $"signatureHash={signatureHash}; statusFilter={filter.Status}");

        var result = await _signatureReplayService.StartAsync(
            OwnerId, new SignatureReplayStartRequest(filter), ResolveRecoveryActor(), cancellationToken);

        if (result.IsFailure)
        {
            _auditLogger.LogCriticalAction(
                HttpContext, OwnerId, action: IntentHeaders.IntentSignatureReplay, outcome: "Denied",
                namespaceId: namespaceId,
                detail: result.Error.Message);
            return ToActionResult(result);
        }

        _logger.LogInformation(
            "Signature replay job {JobId} created by {OwnerId} for namespace {NamespaceId}, signature {SignatureHash}",
            result.Value.Id, OwnerId, namespaceId, LogRedactor.SanitiseForLog(signatureHash));

        return AcceptedAtAction(nameof(GetJob), new { id = result.Value.Id }, result.Value);
    }

    /// <summary>
    /// Lists past and in-flight replay jobs for one failure signature, most recent first —
    /// the durable replay-history record for that signature.
    /// </summary>
    /// <param name="namespaceId">The namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's stable identity hash.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Page size (clamped to 1–100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("~/" + ApiRoutes.Dlq.SignatureReplayHistory)]
    [RequireNamespaceOwnership]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(PaginatedResponse<BulkOperationJobResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaginatedResponse<BulkOperationJobResponse>>> History(
        Guid namespaceId,
        string signatureHash,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _signatureReplayService.ListJobsAsync(
            OwnerId, namespaceId, signatureHash, page, pageSize, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>Gets a single signature-replay job's current status and progress.</summary>
    /// <param name="id">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("~/" + ApiRoutes.SignatureReplayJobs.ById)]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(BulkOperationJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BulkOperationJobResponse>> GetJob(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _signatureReplayService.GetJobAsync(OwnerId, id, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Requests cancellation of a pending or running signature-replay job. Idempotent —
    /// cancelling an already finished job returns its current (terminal) state rather than an
    /// error.
    /// </summary>
    /// <param name="id">The job ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("~/" + ApiRoutes.SignatureReplayJobs.Cancel)]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(BulkOperationJobResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BulkOperationJobResponse>> Cancel(
        Guid id, CancellationToken cancellationToken = default)
    {
        var result = await _signatureReplayService.CancelJobAsync(OwnerId, id, cancellationToken);
        return ToActionResult(result);
    }
}
