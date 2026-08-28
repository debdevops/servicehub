using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for deterministic, same-provider proactive correlation (roadmap §5.D, C1).
/// Unlike <see cref="AnomaliesController"/> and <see cref="DriftFindingsController"/>, detection
/// here is inherently cross-namespace — a correlation only exists when two or more of a caller's
/// namespaces are anomalous together — so this controller runs over every namespace the caller
/// can access rather than a single <c>namespaceId</c>.
/// </summary>
[Route(ApiRoutes.CorrelationFindings.Base)]
[Tags("CorrelationFindings")]
public sealed class CorrelationFindingsController : ApiControllerBase
{
    private readonly IAnomalyDetectionService _anomalyDetectionService;
    private readonly ICorrelationDetectionService _correlationDetectionService;
    private readonly ICorrelationResultCache _correlationResultCache;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<CorrelationFindingsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CorrelationFindingsController"/> class.
    /// </summary>
    public CorrelationFindingsController(
        IAnomalyDetectionService anomalyDetectionService,
        ICorrelationDetectionService correlationDetectionService,
        ICorrelationResultCache correlationResultCache,
        INamespaceRepository namespaceRepository,
        ILogger<CorrelationFindingsController> logger)
    {
        _anomalyDetectionService = anomalyDetectionService ?? throw new ArgumentNullException(nameof(anomalyDetectionService));
        _correlationDetectionService = correlationDetectionService ?? throw new ArgumentNullException(nameof(correlationDetectionService));
        _correlationResultCache = correlationResultCache ?? throw new ArgumentNullException(nameof(correlationResultCache));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Detects same-provider correlations across every namespace the caller can access, within a
    /// specified time window.
    /// </summary>
    /// <param name="startTime">The start of the analysis window (defaults to 24 hours ago).</param>
    /// <param name="endTime">The end of the analysis window (defaults to now).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of detected correlation findings.</returns>
    /// <response code="200">Correlations detected successfully.</response>
    /// <response code="400">endTime was not after startTime.</response>
    [RequireScope(ApiKeyScopes.CorrelationFindingsRead)]
    [HttpPost("detect")]
    [ProducesResponseType(typeof(CorrelationDetectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<CorrelationDetectionResponse>> DetectCorrelations(
        [FromQuery] DateTimeOffset? startTime = null,
        [FromQuery] DateTimeOffset? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var start = startTime ?? DateTimeOffset.UtcNow.AddHours(-24);
        var end = endTime ?? DateTimeOffset.UtcNow;

        // Validated once, upfront: unlike AnomaliesController/DriftFindingsController (which
        // validate a single call into their detection service and surface its failure directly),
        // this endpoint calls DetectAnomaliesAsync once per namespace and only logs+skips
        // per-namespace failures — without this check, a malformed window would silently fail
        // validation identically in every namespace and come back as 200 OK with an empty list
        // instead of a 400.
        if (end <= start)
        {
            return ToActionResult<CorrelationDetectionResponse>(ServiceHub.Shared.Results.Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "endTime must be after startTime."));
        }

        var namespacesResult = await _namespaceRepository.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        if (namespacesResult.IsFailure)
        {
            return ToActionResult<CorrelationDetectionResponse>(namespacesResult.Error);
        }

        var namespaces = namespacesResult.Value.Where(n => n.IsActive).ToList();

        var observations = new List<AnomalyObservation>();
        foreach (var ns in namespaces)
        {
            var anomalyResult = await _anomalyDetectionService
                .DetectAnomaliesAsync(ns.Id, start, end, cancellationToken)
                .ConfigureAwait(false);

            if (anomalyResult.IsFailure)
            {
                _logger.LogWarning(
                    "Anomaly detection failed for namespace {NamespaceId} during correlation detect: {Error}",
                    ns.Id,
                    anomalyResult.Error.Message);
                continue;
            }

            // Tag with the namespace's own true owner, not the caller's OwnerId — a caller may
            // reach this endpoint with access to namespaces shared from a different true owner,
            // and those must never be merged into the same correlation group as the caller's own
            // namespaces (that would be a tenant-isolation bug, not a feature).
            observations.AddRange(anomalyResult.Value.Select(a => new AnomalyObservation(a, ns.OwnerId, ns.Provider)));
        }

        var findings = _correlationDetectionService.DetectCorrelations(observations);

        // Cache the results so a subsequent GET /{id} can retrieve one of them (see
        // ICorrelationResultCache for why this isn't backed by the database).
        _correlationResultCache.Store(findings);

        var findingInfos = findings.Select(MapToCorrelationFindingInfo).ToList();

        _logger.LogInformation(
            "Detected {FindingCount} correlation(s) for owner {OwnerId} across {NamespaceCount} namespace(s)",
            findingInfos.Count,
            OwnerId,
            namespaces.Count);

        return Ok(new CorrelationDetectionResponse(
            StartTime: start,
            EndTime: end,
            Findings: findingInfos,
            DetectedAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Gets a specific correlation finding by ID.
    /// </summary>
    /// <param name="id">The correlation finding ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The correlation finding details.</returns>
    /// <response code="200">Correlation finding retrieved successfully.</response>
    /// <response code="404">Correlation finding not found.</response>
    [RequireScope(ApiKeyScopes.CorrelationFindingsRead)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(CorrelationFindingInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CorrelationFindingInfo>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting correlation finding {CorrelationFindingId}", id);

        var finding = _correlationResultCache.TryGet(id);
        if (finding is null)
        {
            return NotFoundResult(id);
        }

        // TENANT ISOLATION: every member namespace must be accessible to the caller — a
        // correlation can combine namespaces the caller only reaches via sharing, and if even one
        // member isn't accessible, exposing the finding would leak that namespace's existence.
        foreach (var namespaceId in finding.Members.Select(m => m.NamespaceId).Distinct())
        {
            var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
            if (namespaceResult.IsFailure || !namespaceResult.Value.IsAccessibleBy(OwnerId, AllowedNamespaceIds))
            {
                return NotFoundResult(id);
            }
        }

        return Ok(MapToCorrelationFindingInfo(finding));
    }

    private ActionResult<CorrelationFindingInfo> NotFoundResult(Guid id) =>
        ToActionResult<CorrelationFindingInfo>(ServiceHub.Shared.Results.Error.NotFound(
            "CorrelationFinding.NotFound",
            $"Correlation finding with ID '{id}' was not found."));

    private static CorrelationFindingInfo MapToCorrelationFindingInfo(CorrelationFinding finding)
    {
        return new CorrelationFindingInfo(
            Id: finding.Id,
            Provider: finding.Provider.ToString(),
            Members: finding.Members
                .Select(m => new CorrelationMemberInfo(m.NamespaceId, m.EntityName, m.AnomalyType.ToString(), m.Severity))
                .ToList(),
            Severity: finding.Severity,
            Description: finding.Description,
            DetectedAt: finding.DetectedAt,
            Metrics: finding.Metrics,
            RecommendedActions: finding.RecommendedActions);
    }
}

/// <summary>
/// One entity's contribution to a correlation finding.
/// </summary>
public sealed record CorrelationMemberInfo(
    Guid NamespaceId,
    string EntityName,
    string AnomalyType,
    int Severity);

/// <summary>
/// Information about a detected correlation finding.
/// </summary>
public sealed record CorrelationFindingInfo(
    Guid Id,
    string Provider,
    IReadOnlyList<CorrelationMemberInfo> Members,
    int Severity,
    string Description,
    DateTimeOffset DetectedAt,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyList<string> RecommendedActions);

/// <summary>
/// Response model for correlation detection results.
/// </summary>
public sealed record CorrelationDetectionResponse(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<CorrelationFindingInfo> Findings,
    DateTimeOffset DetectedAt);
