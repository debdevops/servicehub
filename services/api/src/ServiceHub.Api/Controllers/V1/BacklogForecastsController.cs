using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for deterministic, arithmetic backlog-growth forecasting (roadmap §5.E, P4 —
/// "Predictive backlog signal"). Provides endpoints for computing and retrieving projected
/// alert-threshold breaches.
/// </summary>
[Route(ApiRoutes.BacklogForecasts.Base)]
[Tags("BacklogForecasts")]
public sealed class BacklogForecastsController : ApiControllerBase
{
    private readonly IBacklogForecastService _backlogForecastService;
    private readonly IBacklogForecastResultCache _backlogForecastResultCache;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<BacklogForecastsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="BacklogForecastsController"/> class.
    /// </summary>
    /// <param name="backlogForecastService">The deterministic backlog forecast service.</param>
    /// <param name="backlogForecastResultCache">Short-lived cache of recently computed forecasts.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="logger">The logger.</param>
    public BacklogForecastsController(
        IBacklogForecastService backlogForecastService,
        IBacklogForecastResultCache backlogForecastResultCache,
        INamespaceRepository namespaceRepository,
        ILogger<BacklogForecastsController> logger)
    {
        _backlogForecastService = backlogForecastService ?? throw new ArgumentNullException(nameof(backlogForecastService));
        _backlogForecastResultCache = backlogForecastResultCache ?? throw new ArgumentNullException(nameof(backlogForecastResultCache));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Forecasts backlog-threshold breaches in a namespace within a specified analysis window.
    /// </summary>
    /// <param name="namespaceId">The namespace ID.</param>
    /// <param name="startTime">The start of the trend window (defaults to 24 hours ago).</param>
    /// <param name="endTime">The end of the trend window (defaults to now).</param>
    /// <param name="alertThreshold">The backlog depth considered a breach (defaults to the service's configured default).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A list of projected backlog forecasts.</returns>
    /// <response code="200">Forecasts computed successfully.</response>
    /// <response code="404">Namespace not found.</response>
    [RequireScope(ApiKeyScopes.BacklogForecastsRead)]
    [HttpPost("forecast")]
    [ProducesResponseType(typeof(BacklogForecastResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BacklogForecastResponse>> Forecast(
        [FromQuery] Guid namespaceId,
        [FromQuery] DateTimeOffset? startTime = null,
        [FromQuery] DateTimeOffset? endTime = null,
        [FromQuery] int? alertThreshold = null,
        CancellationToken cancellationToken = default)
    {
        var start = startTime ?? DateTimeOffset.UtcNow.AddHours(-24);
        var end = endTime ?? DateTimeOffset.UtcNow;

        _logger.LogInformation(
            "Forecasting backlog growth for namespace {NamespaceId} from {StartTime} to {EndTime}",
            namespaceId,
            start,
            end);

        // Verify namespace exists and belongs to the current owner
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
        {
            return ToActionResult<BacklogForecastResponse>(namespaceResult.Error);
        }

        var result = await _backlogForecastService.ForecastAsync(
            namespaceId,
            start,
            end,
            alertThreshold,
            cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<BacklogForecastResponse>(result.Error);
        }

        // Cache the results so a subsequent GET /{id} can retrieve one of them (see
        // IBacklogForecastResultCache for why this isn't backed by the database).
        _backlogForecastResultCache.Store(result.Value);

        var forecasts = result.Value
            .Select(MapToBacklogForecastInfo)
            .ToList();

        _logger.LogInformation(
            "Projected {ForecastCount} backlog breach(es) for namespace {NamespaceId}",
            forecasts.Count,
            namespaceId);

        return Ok(new BacklogForecastResponse(
            NamespaceId: namespaceId,
            StartTime: start,
            EndTime: end,
            Forecasts: forecasts,
            DetectedAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Gets a specific backlog forecast by ID.
    /// </summary>
    /// <param name="id">The backlog forecast ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The backlog forecast details.</returns>
    /// <response code="200">Forecast retrieved successfully.</response>
    /// <response code="404">Forecast not found.</response>
    [RequireScope(ApiKeyScopes.BacklogForecastsRead)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BacklogForecastInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<BacklogForecastInfo>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting backlog forecast {ForecastId}", id);

        var forecast = _backlogForecastResultCache.TryGet(id);
        if (forecast is null)
        {
            return ToActionResult<BacklogForecastInfo>(ServiceHub.Shared.Results.Error.NotFound(
                "BacklogForecast.NotFound",
                $"Backlog forecast with ID '{id}' was not found."));
        }

        // TENANT ISOLATION: a forecast is only visible to the owner of the namespace it was
        // computed for. Return 404 (not 403) on mismatch to avoid leaking that the ID exists.
        var namespaceResult = await _namespaceRepository.GetByIdAsync(forecast.NamespaceId, cancellationToken);
        if (namespaceResult.IsFailure
            && namespaceResult.Error.Type != ServiceHub.Shared.Results.ErrorType.NotFound)
        {
            return ToActionResult<BacklogForecastInfo>(namespaceResult.Error);
        }

        if (namespaceResult.IsFailure
            || !string.Equals(namespaceResult.Value.OwnerId, OwnerId, StringComparison.Ordinal))
        {
            return ToActionResult<BacklogForecastInfo>(ServiceHub.Shared.Results.Error.NotFound(
                "BacklogForecast.NotFound",
                $"Backlog forecast with ID '{id}' was not found."));
        }

        return Ok(MapToBacklogForecastInfo(forecast));
    }

    /// <summary>
    /// Maps a BacklogForecast entity to a BacklogForecastInfo DTO.
    /// </summary>
    /// <param name="forecast">The backlog forecast entity.</param>
    /// <returns>The backlog forecast info.</returns>
    private static BacklogForecastInfo MapToBacklogForecastInfo(BacklogForecast forecast)
    {
        return new BacklogForecastInfo(
            Id: forecast.Id,
            NamespaceId: forecast.NamespaceId,
            EntityName: forecast.EntityName,
            CurrentBacklogCount: forecast.CurrentBacklogCount,
            GrowthRatePerHour: forecast.GrowthRatePerHour,
            AlertThreshold: forecast.AlertThreshold,
            ProjectedHoursToBreach: forecast.ProjectedHoursToBreach,
            ProjectedBreachAtUtc: forecast.ProjectedBreachAtUtc,
            Severity: forecast.Severity,
            Description: forecast.Description,
            DetectedAt: forecast.DetectedAt,
            Metrics: forecast.Metrics,
            RecommendedActions: forecast.RecommendedActions);
    }
}

/// <summary>
/// Information about a projected backlog forecast.
/// </summary>
/// <param name="Id">The forecast ID.</param>
/// <param name="NamespaceId">The namespace ID.</param>
/// <param name="EntityName">The entity name.</param>
/// <param name="CurrentBacklogCount">The entity's active DLQ message count at forecast time.</param>
/// <param name="GrowthRatePerHour">The extrapolated growth rate, in messages per hour.</param>
/// <param name="AlertThreshold">The alert threshold this forecast projects a breach against.</param>
/// <param name="ProjectedHoursToBreach">Projected hours until the threshold is crossed.</param>
/// <param name="ProjectedBreachAtUtc">Projected UTC timestamp of the threshold breach.</param>
/// <param name="Severity">The severity level (0-100).</param>
/// <param name="Description">The forecast description.</param>
/// <param name="DetectedAt">When the forecast was computed.</param>
/// <param name="Metrics">Associated metrics.</param>
/// <param name="RecommendedActions">Recommended actions.</param>
public sealed record BacklogForecastInfo(
    Guid Id,
    Guid NamespaceId,
    string EntityName,
    int CurrentBacklogCount,
    double GrowthRatePerHour,
    int AlertThreshold,
    double ProjectedHoursToBreach,
    DateTimeOffset ProjectedBreachAtUtc,
    int Severity,
    string Description,
    DateTimeOffset DetectedAt,
    IReadOnlyDictionary<string, double> Metrics,
    IReadOnlyList<string> RecommendedActions);

/// <summary>
/// Response model for backlog forecast results.
/// </summary>
/// <param name="NamespaceId">The namespace ID.</param>
/// <param name="StartTime">The analysis start time.</param>
/// <param name="EndTime">The analysis end time.</param>
/// <param name="Forecasts">The projected forecasts.</param>
/// <param name="DetectedAt">When the forecast computation was performed.</param>
public sealed record BacklogForecastResponse(
    Guid NamespaceId,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<BacklogForecastInfo> Forecasts,
    DateTimeOffset DetectedAt);
