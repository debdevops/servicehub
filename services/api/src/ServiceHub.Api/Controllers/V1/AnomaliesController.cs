using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for deterministic, statistics-based anomaly detection in Service Bus traffic
/// (roadmap §5.B, I3). Provides endpoints for detecting and retrieving anomalies.
/// </summary>
[Route(ApiRoutes.Anomalies.Base)]
[Tags("Anomalies")]
public sealed class AnomaliesController : ApiControllerBase
{
    private readonly IAnomalyDetectionService _anomalyDetectionService;
    private readonly IAnomalyResultCache _anomalyResultCache;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<AnomaliesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnomaliesController"/> class.
    /// </summary>
    /// <param name="anomalyDetectionService">The deterministic anomaly detection service.</param>
    /// <param name="anomalyResultCache">Short-lived cache of recently detected anomalies.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="logger">The logger.</param>
    public AnomaliesController(
        IAnomalyDetectionService anomalyDetectionService,
        IAnomalyResultCache anomalyResultCache,
        INamespaceRepository namespaceRepository,
        ILogger<AnomaliesController> logger)
    {
        _anomalyDetectionService = anomalyDetectionService ?? throw new ArgumentNullException(nameof(anomalyDetectionService));
        _anomalyResultCache = anomalyResultCache ?? throw new ArgumentNullException(nameof(anomalyResultCache));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Detects anomalies in a namespace within a specified time window.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="startTime">The start of the analysis window (defaults to 1 hour ago).</param>
    /// <param name="endTime">The end of the analysis window (defaults to now).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of detected anomalies.</returns>
    /// <response code="200">Anomalies detected successfully.</response>
    /// <response code="404">Namespace not found.</response>
    [RequireScope(ApiKeyScopes.AnomaliesRead)]
    [HttpPost("detect")]
    [ProducesResponseType(typeof(AnomalyDetectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AnomalyDetectionResponse>> DetectAnomalies(
        [FromQuery] Guid namespaceId,
        [FromQuery] DateTimeOffset? startTime = null,
        [FromQuery] DateTimeOffset? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var start = startTime ?? DateTimeOffset.UtcNow.AddHours(-1);
        var end = endTime ?? DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Detecting anomalies for namespace {NamespaceId} from {StartTime} to {EndTime}",
            namespaceId,
            start,
            end);

        // Verify namespace exists and belongs to the current owner
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<AnomalyDetectionResponse>(namespaceResult.Error);
        }

        var result = await _anomalyDetectionService.DetectAnomaliesAsync(
            namespaceId,
            start,
            end,
            cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<AnomalyDetectionResponse>(result.Error);
        }

        // Cache the results so a subsequent GET /{id} can retrieve one of them (see
        // IAnomalyResultCache for why this isn't backed by the database).
        _anomalyResultCache.Store(result.Value);

        var anomalies = result.Value
            .Select(MapToAnomalyInfo)
            .ToList();

        _logger.LogInformation(
            "Detected {AnomalyCount} anomalies for namespace {NamespaceId}",
            anomalies.Count,
            namespaceId);

        return Ok(new AnomalyDetectionResponse(
            NamespaceId: namespaceId,
            StartTime: start,
            EndTime: end,
            Anomalies: anomalies,
            DetectedAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Gets a specific anomaly by ID.
    /// </summary>
    /// <param name="id">The anomaly ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The anomaly details.</returns>
    /// <response code="200">Anomaly retrieved successfully.</response>
    /// <response code="404">Anomaly not found.</response>
    [RequireScope(ApiKeyScopes.AnomaliesRead)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AnomalyInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AnomalyInfo>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting anomaly {AnomalyId}", id);

        var anomaly = _anomalyResultCache.TryGet(id);
        if (anomaly is null)
        {
            return ToActionResult<AnomalyInfo>(ServiceHub.Shared.Results.Error.NotFound(
                "Anomaly.NotFound",
                $"Anomaly with ID '{id}' was not found."));
        }

        // TENANT ISOLATION: an anomaly is only visible to the owner of the namespace
        // it was detected in. Return 404 (not 403) on mismatch to avoid leaking that
        // the anomaly ID exists.
        var namespaceResult = await _namespaceRepository.GetByIdAsync(anomaly.NamespaceId, cancellationToken);
        if (namespaceResult.IsFailure
            && namespaceResult.Error.Type != ServiceHub.Shared.Results.ErrorType.NotFound)
        {
            return ToActionResult<AnomalyInfo>(namespaceResult.Error);
        }

        if (namespaceResult.IsFailure
            || !string.Equals(namespaceResult.Value.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return ToActionResult<AnomalyInfo>(ServiceHub.Shared.Results.Error.NotFound(
                "Anomaly.NotFound",
                $"Anomaly with ID '{id}' was not found."));
        }

        return Ok(MapToAnomalyInfo(anomaly));
    }

    /// <summary>
    /// Maps an Anomaly entity to an AnomalyInfo DTO.
    /// </summary>
    /// <param name="anomaly">The anomaly entity.</param>
    /// <returns>The anomaly info.</returns>
    private static AnomalyInfo MapToAnomalyInfo(Anomaly anomaly)
    {
        return new AnomalyInfo(
            Id: anomaly.Id,
            NamespaceId: anomaly.NamespaceId,
            EntityName: anomaly.EntityName,
            Type: anomaly.Type.ToString(),
            Severity: anomaly.Severity,
            Description: anomaly.Description,
            DetectedAt: anomaly.DetectedAt,
            Metrics: anomaly.Metrics,
            RecommendedActions: anomaly.RecommendedActions);
    }
}

/// <summary>
/// Information about a detected anomaly.
/// </summary>
/// <param name="Id">The anomaly ID.</param>
/// <param name="NamespaceId">The namespace ID.</param>
/// <param name="EntityName">The entity name.</param>
/// <param name="Type">The anomaly type.</param>
/// <param name="Severity">The severity level (0-100).</param>
/// <param name="Description">The anomaly description.</param>
/// <param name="DetectedAt">When the anomaly was detected.</param>
/// <param name="Metrics">Associated metrics.</param>
/// <param name="RecommendedActions">Recommended actions.</param>
public sealed record AnomalyInfo(
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
/// Response model for anomaly detection results.
/// </summary>
/// <param name="NamespaceId">The namespace ID.</param>
/// <param name="StartTime">The analysis start time.</param>
/// <param name="EndTime">The analysis end time.</param>
/// <param name="Anomalies">The detected anomalies.</param>
/// <param name="DetectedAt">When the detection was performed.</param>
public sealed record AnomalyDetectionResponse(
    Guid NamespaceId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<AnomalyInfo> Anomalies,
    DateTimeOffset DetectedAt);
