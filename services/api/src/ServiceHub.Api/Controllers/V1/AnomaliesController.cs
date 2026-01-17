using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for AI-powered anomaly detection in Service Bus traffic.
/// Provides endpoints for detecting and retrieving anomalies.
/// </summary>
[Route(ApiRoutes.Anomalies.Base)]
[Tags("Anomalies")]
public sealed class AnomaliesController : ApiControllerBase
{
    private readonly IAIServiceClient _aiServiceClient;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<AnomaliesController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AnomaliesController"/> class.
    /// </summary>
    /// <param name="aiServiceClient">The AI service client.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="logger">The logger.</param>
    public AnomaliesController(
        IAIServiceClient aiServiceClient,
        INamespaceRepository namespaceRepository,
        ILogger<AnomaliesController> logger)
    {
        _aiServiceClient = aiServiceClient ?? throw new ArgumentNullException(nameof(aiServiceClient));
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
    /// <response code="503">AI service unavailable.</response>
    [RequireScope(ApiKeyScopes.AnomaliesRead)]
    [HttpPost("detect")]
    [ProducesResponseType(typeof(AnomalyDetectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
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

        // Verify namespace exists
        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<AnomalyDetectionResponse>(namespaceResult.Error);
        }

        // Check AI service availability
        var availabilityResult = await _aiServiceClient.IsAvailableAsync(cancellationToken);
        if (availabilityResult.IsFailure || !availabilityResult.Value)
        {
            return StatusCode(
                StatusCodes.Status503ServiceUnavailable,
                new ProblemDetails
                {
                    Status = StatusCodes.Status503ServiceUnavailable,
                    Title = "AI Service Unavailable",
                    Detail = "The AI anomaly detection service is currently unavailable."
                });
        }

        // Detect anomalies
        var result = await _aiServiceClient.DetectAnomaliesAsync(
            namespaceId,
            start,
            end,
            cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<AnomalyDetectionResponse>(result.Error);
        }

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
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(AnomalyInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AnomalyInfo>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting anomaly {AnomalyId}", id);

        var result = await _aiServiceClient.GetAnomalyByIdAsync(id, cancellationToken);
        if (result.IsFailure)
        {
            return ToActionResult<AnomalyInfo>(result.Error);
        }

        return Ok(MapToAnomalyInfo(result.Value));
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
