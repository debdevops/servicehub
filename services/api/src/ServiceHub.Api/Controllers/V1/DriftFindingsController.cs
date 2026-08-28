using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for deterministic message-shape baseline and drift detection
/// (roadmap §5.C, P1/P2). Provides endpoints for detecting and retrieving drift findings.
/// </summary>
[Route(ApiRoutes.DriftFindings.Base)]
[Tags("DriftFindings")]
public sealed class DriftFindingsController : ApiControllerBase
{
    private readonly IDriftDetectionService _driftDetectionService;
    private readonly IDriftResultCache _driftResultCache;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<DriftFindingsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DriftFindingsController"/> class.
    /// </summary>
    /// <param name="driftDetectionService">The deterministic drift detection service.</param>
    /// <param name="driftResultCache">Short-lived cache of recently detected drift findings.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="logger">The logger.</param>
    public DriftFindingsController(
        IDriftDetectionService driftDetectionService,
        IDriftResultCache driftResultCache,
        INamespaceRepository namespaceRepository,
        ILogger<DriftFindingsController> logger)
    {
        _driftDetectionService = driftDetectionService ?? throw new ArgumentNullException(nameof(driftDetectionService));
        _driftResultCache = driftResultCache ?? throw new ArgumentNullException(nameof(driftResultCache));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Detects message-shape drift in a namespace within a specified time window.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="startTime">The start of the analysis window (defaults to 24 hours ago).</param>
    /// <param name="endTime">The end of the analysis window (defaults to now).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of detected drift findings.</returns>
    /// <response code="200">Drift findings detected successfully.</response>
    /// <response code="404">Namespace not found.</response>
    [RequireScope(ApiKeyScopes.DriftFindingsRead)]
    [HttpPost("detect")]
    [ProducesResponseType(typeof(DriftDetectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriftDetectionResponse>> DetectDrift(
        [FromQuery] Guid namespaceId,
        [FromQuery] DateTimeOffset? startTime = null,
        [FromQuery] DateTimeOffset? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var start = startTime ?? DateTimeOffset.UtcNow.AddHours(-24);
        var end = endTime ?? DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Detecting drift for namespace {NamespaceId} from {StartTime} to {EndTime}",
            namespaceId,
            start,
            end);

        // Verify namespace exists and belongs to the current owner
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<DriftDetectionResponse>(namespaceResult.Error);
        }

        var result = await _driftDetectionService.DetectDriftAsync(
            namespaceId,
            start,
            end,
            cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<DriftDetectionResponse>(result.Error);
        }

        // Cache the results so a subsequent GET /{id} can retrieve one of them (see
        // IDriftResultCache for why this isn't backed by the database).
        _driftResultCache.Store(result.Value);

        var findings = result.Value
            .Select(MapToDriftFindingInfo)
            .ToList();

        _logger.LogInformation(
            "Detected {FindingCount} drift finding(s) for namespace {NamespaceId}",
            findings.Count,
            namespaceId);

        return Ok(new DriftDetectionResponse(
            NamespaceId: namespaceId,
            StartTime: start,
            EndTime: end,
            Findings: findings,
            DetectedAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Gets a specific drift finding by ID.
    /// </summary>
    /// <param name="id">The drift finding ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The drift finding details.</returns>
    /// <response code="200">Drift finding retrieved successfully.</response>
    /// <response code="404">Drift finding not found.</response>
    [RequireScope(ApiKeyScopes.DriftFindingsRead)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(DriftFindingInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DriftFindingInfo>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting drift finding {DriftFindingId}", id);

        var finding = _driftResultCache.TryGet(id);
        if (finding is null)
        {
            return ToActionResult<DriftFindingInfo>(ServiceHub.Shared.Results.Error.NotFound(
                "DriftFinding.NotFound",
                $"Drift finding with ID '{id}' was not found."));
        }

        // TENANT ISOLATION: a drift finding is only visible to callers who can access the
        // namespace it was detected in — the same owner-or-shared-with check DetectDrift already
        // applies via GetOwnedNamespaceAsync, so a shared-namespace collaborator who can trigger
        // detection can also retrieve the finding it produced. Return 404 (not 403) on mismatch
        // to avoid leaking that the finding ID exists.
        var namespaceResult = await _namespaceRepository.GetByIdAsync(finding.NamespaceId, cancellationToken);
        if (namespaceResult.IsFailure
            && namespaceResult.Error.Type != ServiceHub.Shared.Results.ErrorType.NotFound)
        {
            return ToActionResult<DriftFindingInfo>(namespaceResult.Error);
        }

        if (namespaceResult.IsFailure
            || !namespaceResult.Value.IsAccessibleBy(OwnerId, AllowedNamespaceIds))
        {
            return ToActionResult<DriftFindingInfo>(ServiceHub.Shared.Results.Error.NotFound(
                "DriftFinding.NotFound",
                $"Drift finding with ID '{id}' was not found."));
        }

        return Ok(MapToDriftFindingInfo(finding));
    }

    /// <summary>
    /// Maps a DriftFinding entity to a DriftFindingInfo DTO.
    /// </summary>
    /// <param name="finding">The drift finding entity.</param>
    /// <returns>The drift finding info.</returns>
    private static DriftFindingInfo MapToDriftFindingInfo(DriftFinding finding)
    {
        return new DriftFindingInfo(
            Id: finding.Id,
            NamespaceId: finding.NamespaceId,
            EntityName: finding.EntityName,
            Type: finding.Type.ToString(),
            Severity: finding.Severity,
            Description: finding.Description,
            DetectedAt: finding.DetectedAt,
            Metrics: finding.Metrics,
            RecommendedActions: finding.RecommendedActions);
    }
}

/// <summary>
/// Information about a detected drift finding.
/// </summary>
/// <param name="Id">The drift finding ID.</param>
/// <param name="NamespaceId">The namespace ID.</param>
/// <param name="EntityName">The entity name.</param>
/// <param name="Type">The drift type.</param>
/// <param name="Severity">The severity level (0-100).</param>
/// <param name="Description">The finding description.</param>
/// <param name="DetectedAt">When the finding was detected.</param>
/// <param name="Metrics">Associated metrics.</param>
/// <param name="RecommendedActions">Recommended actions.</param>
public sealed record DriftFindingInfo(
    Guid Id,
    Guid NamespaceId,
    string EntityName,
    string Type,
    int Severity,
    string Description,
    DateTimeOffset DetectedAt,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyList<string> RecommendedActions);

/// <summary>
/// Response model for drift detection results.
/// </summary>
/// <param name="NamespaceId">The namespace ID.</param>
/// <param name="StartTime">The analysis start time.</param>
/// <param name="EndTime">The analysis end time.</param>
/// <param name="Findings">The detected drift findings.</param>
/// <param name="DetectedAt">When the detection was performed.</param>
public sealed record DriftDetectionResponse(
    Guid NamespaceId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<DriftFindingInfo> Findings,
    DateTimeOffset DetectedAt);
