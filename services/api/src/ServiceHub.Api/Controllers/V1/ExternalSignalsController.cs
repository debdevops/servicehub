using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Filters;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for C3 — external-signal correlation (roadmap §5.D; M5, ADR-0008): recording/listing
/// deploy/config-change signals, and correlating anomaly onset against them. Structurally the same
/// shape as <see cref="CorrelationFindingsController"/> (C1/C2) — a detect endpoint that computes
/// and caches results, and a by-ID lookup — generalized to correlate against a durably recorded
/// external signal instead of another entity's anomaly.
/// </summary>
/// <remarks>
/// <see cref="RequireNamespaceOwnershipAttribute"/> is applied class-wide as defense-in-depth for
/// <see cref="GetSignals"/>'s optional <c>namespaceId</c> query filter — same convention
/// <see cref="RecoveryController"/> uses for its own optional <c>namespaceId</c> filters. The
/// underlying repository query is already owner-scoped (a foreign namespaceId would just return
/// zero rows, never another owner's data), but this keeps the tenant-isolation check declared once
/// rather than relying solely on that being true today.
/// </remarks>
[Route(ApiRoutes.ExternalSignals.Base)]
[Tags("ExternalSignals")]
[RequireNamespaceOwnership]
public sealed class ExternalSignalsController : ApiControllerBase
{
    private const int DefaultLimit = 100;
    private const int MaxLimit = 500;
    private const int DefaultCorrelationWindowHours = 6;
    private const int MaxCorrelationWindowHours = 168;

    private readonly IExternalSignalRepository _externalSignalRepository;
    private readonly IAnomalyDetectionService _anomalyDetectionService;
    private readonly IExternalSignalCorrelationService _correlationService;
    private readonly IExternalSignalCorrelationCache _correlationCache;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<ExternalSignalsController> _logger;

    /// <summary>Initializes a new instance of the <see cref="ExternalSignalsController"/> class.</summary>
    public ExternalSignalsController(
        IExternalSignalRepository externalSignalRepository,
        IAnomalyDetectionService anomalyDetectionService,
        IExternalSignalCorrelationService correlationService,
        IExternalSignalCorrelationCache correlationCache,
        INamespaceRepository namespaceRepository,
        ILogger<ExternalSignalsController> logger)
    {
        _externalSignalRepository = externalSignalRepository ?? throw new ArgumentNullException(nameof(externalSignalRepository));
        _anomalyDetectionService = anomalyDetectionService ?? throw new ArgumentNullException(nameof(anomalyDetectionService));
        _correlationService = correlationService ?? throw new ArgumentNullException(nameof(correlationService));
        _correlationCache = correlationCache ?? throw new ArgumentNullException(nameof(correlationCache));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Records a deploy/config-change signal — a manual annotation or a webhook's ingest. Never
    /// itself a correlation; see <see cref="DetectCorrelations"/>.
    /// </summary>
    /// <param name="request">The signal to record.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="400">
    /// <paramref name="request"/> failed validation, or <see cref="RecordExternalSignalHttpRequest.NamespaceId"/>
    /// does not resolve to a namespace the caller can access.
    /// </response>
    [RequireScope(ApiKeyScopes.ExternalSignalsWrite)]
    [HttpPost]
    [ProducesResponseType(typeof(ExternalSignalEventResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExternalSignalEventResponse>> RecordSignal(
        [FromBody] RecordExternalSignalHttpRequest request,
        CancellationToken cancellationToken = default)
    {
        // RequireNamespaceOwnershipAttribute only inspects route/query values (see its own
        // remarks) — request.NamespaceId arrives in the body, so it needs the same inline
        // ownership check every other namespace-scoped write in this codebase uses (e.g.
        // RulesController.Create/Update) rather than being trusted as-is.
        if (request.NamespaceId is { } requestedNamespaceId)
        {
            var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, requestedNamespaceId, cancellationToken);
            if (namespaceResult.IsFailure)
            {
                return ToActionResult<ExternalSignalEventResponse>(Error.Validation(
                    "ExternalSignal.NamespaceInvalid",
                    $"Namespace '{requestedNamespaceId}' does not exist or is not accessible."));
            }
        }

        var result = await _externalSignalRepository.RecordAsync(new RecordExternalSignalRequest
        {
            OwnerId = OwnerId,
            NamespaceId = request.NamespaceId,
            SignalType = request.SignalType,
            OccurredAt = request.OccurredAt,
            Source = request.Source,
            DetailJson = request.DetailJson,
        }, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<ExternalSignalEventResponse>(result.Error);
        }

        return Ok(MapToResponse(result.Value));
    }

    /// <summary>
    /// Lists recorded external signals for the caller, most recent first.
    /// </summary>
    /// <param name="namespaceId">Optional namespace filter — matches only signals scoped to
    /// exactly this namespace, not fleet-wide signals.</param>
    /// <param name="startTime">The start of the query window (defaults to 7 days ago).</param>
    /// <param name="endTime">The end of the query window (defaults to now).</param>
    /// <param name="limit">Maximum number of signals to return (1-500, default 100).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.ExternalSignalsRead)]
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<ExternalSignalEventResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ExternalSignalEventResponse>>> GetSignals(
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] DateTimeOffset? startTime = null,
        [FromQuery] DateTimeOffset? endTime = null,
        [FromQuery] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var start = startTime ?? DateTimeOffset.UtcNow.AddDays(-7);
        var end = endTime ?? DateTimeOffset.UtcNow;

        var signals = await _externalSignalRepository.QueryAsync(
            OwnerId, namespaceId, start, end, ClampLimit(limit), cancellationToken);

        return Ok(signals.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Detects external-signal correlations across every namespace the caller can access, within
    /// a specified analysis window: for each anomaly found, the nearest recorded signal that
    /// preceded its onset by no more than <paramref name="correlationWindowHours"/>.
    /// </summary>
    /// <param name="startTime">The start of the anomaly-analysis window (defaults to 24 hours ago).</param>
    /// <param name="endTime">The end of the anomaly-analysis window (defaults to now).</param>
    /// <param name="correlationWindowHours">Maximum gap between a signal and an anomaly's onset
    /// for the two to be considered correlated (1-168, default 6).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <response code="400">endTime was not after startTime.</response>
    [RequireScope(ApiKeyScopes.ExternalSignalsRead)]
    [HttpPost("detect")]
    [ProducesResponseType(typeof(ExternalSignalCorrelationDetectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ExternalSignalCorrelationDetectionResponse>> DetectCorrelations(
        [FromQuery] DateTimeOffset? startTime = null,
        [FromQuery] DateTimeOffset? endTime = null,
        [FromQuery] int correlationWindowHours = DefaultCorrelationWindowHours,
        CancellationToken cancellationToken = default)
    {
        var start = startTime ?? DateTimeOffset.UtcNow.AddHours(-24);
        var end = endTime ?? DateTimeOffset.UtcNow;

        if (end <= start)
        {
            return ToActionResult<ExternalSignalCorrelationDetectionResponse>(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "endTime must be after startTime."));
        }

        var window = TimeSpan.FromHours(Math.Clamp(correlationWindowHours, 1, MaxCorrelationWindowHours));

        var namespacesResult = await _namespaceRepository.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        if (namespacesResult.IsFailure)
        {
            return ToActionResult<ExternalSignalCorrelationDetectionResponse>(namespacesResult.Error);
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
                    "Anomaly detection failed for namespace {NamespaceId} during external-signal correlation: {Error}",
                    ns.Id,
                    anomalyResult.Error.Message);
                continue;
            }

            observations.AddRange(anomalyResult.Value.Select(a => new AnomalyObservation(a, ns.OwnerId, ns.Provider)));
        }

        // Signals may have occurred before startTime and still be within the correlation window
        // of an anomaly detected at the very start of it, so the signal lookback starts one
        // window earlier than the anomaly-analysis window itself.
        var signals = await _externalSignalRepository.QueryAsync(
            OwnerId, namespaceId: null, start - window, end, MaxLimit, cancellationToken);

        var correlations = _correlationService.DetectCorrelations(observations, signals, window);

        _correlationCache.Store(correlations);

        _logger.LogInformation(
            "Detected {CorrelationCount} external-signal correlation(s) for owner {OwnerId} across {NamespaceCount} namespace(s)",
            correlations.Count,
            OwnerId,
            namespaces.Count);

        return Ok(new ExternalSignalCorrelationDetectionResponse(
            StartTime: start,
            EndTime: end,
            CorrelationWindow: window,
            Correlations: correlations.Select(MapToCorrelationInfo).ToList(),
            DetectedAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Gets a specific external-signal correlation by ID.
    /// </summary>
    /// <param name="id">The correlation ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [RequireScope(ApiKeyScopes.ExternalSignalsRead)]
    [HttpGet("correlations/{id:guid}")]
    [ProducesResponseType(typeof(ExternalSignalCorrelationInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ExternalSignalCorrelationInfo>> GetCorrelationById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        var correlation = _correlationCache.TryGet(id);
        if (correlation is null)
        {
            return NotFoundResult(id);
        }

        var namespaceResult = await _namespaceRepository.GetByIdAsync(correlation.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (namespaceResult.IsFailure || !namespaceResult.Value.IsAccessibleBy(OwnerId, AllowedNamespaceIds))
        {
            return NotFoundResult(id);
        }

        return Ok(MapToCorrelationInfo(correlation));
    }

    private ActionResult<ExternalSignalCorrelationInfo> NotFoundResult(Guid id) =>
        ToActionResult<ExternalSignalCorrelationInfo>(Error.NotFound(
            "ExternalSignalCorrelation.NotFound",
            $"External-signal correlation with ID '{id}' was not found."));

    private static int ClampLimit(int limit) => Math.Clamp(limit, 1, MaxLimit);

    private static ExternalSignalEventResponse MapToResponse(ExternalSignalEvent signal) => new(
        Id: signal.Id,
        NamespaceId: signal.NamespaceId,
        SignalType: signal.SignalType.ToString(),
        OccurredAt: signal.OccurredAt,
        Source: signal.Source,
        DetailJson: signal.DetailJson,
        IngestedAt: signal.IngestedAt);

    private static ExternalSignalCorrelationInfo MapToCorrelationInfo(ExternalSignalCorrelation correlation) => new(
        Id: correlation.Id,
        NamespaceId: correlation.NamespaceId,
        EntityName: correlation.EntityName,
        AnomalyType: correlation.AnomalyType.ToString(),
        AnomalySeverity: correlation.AnomalySeverity,
        Provider: correlation.Provider.ToString(),
        SignalId: correlation.SignalId,
        SignalType: correlation.SignalType.ToString(),
        SignalSource: correlation.SignalSource,
        SignalOccurredAt: correlation.SignalOccurredAt,
        GapMinutes: correlation.Gap.TotalMinutes,
        Description: correlation.Description,
        DetectedAt: correlation.DetectedAt,
        RecommendedActions: correlation.RecommendedActions);
}

/// <summary>Response for one recorded external signal.</summary>
public sealed record ExternalSignalEventResponse(
    Guid Id,
    Guid? NamespaceId,
    string SignalType,
    DateTimeOffset OccurredAt,
    string Source,
    string? DetailJson,
    DateTimeOffset IngestedAt);

/// <summary>Information about one detected external-signal correlation.</summary>
public sealed record ExternalSignalCorrelationInfo(
    Guid Id,
    Guid NamespaceId,
    string EntityName,
    string AnomalyType,
    int AnomalySeverity,
    string Provider,
    Guid SignalId,
    string SignalType,
    string SignalSource,
    DateTimeOffset SignalOccurredAt,
    double GapMinutes,
    string Description,
    DateTimeOffset DetectedAt,
    IReadOnlyList<string> RecommendedActions);

/// <summary>Response model for external-signal correlation detection results.</summary>
public sealed record ExternalSignalCorrelationDetectionResponse(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    TimeSpan CorrelationWindow,
    IReadOnlyList<ExternalSignalCorrelationInfo> Correlations,
    DateTimeOffset DetectedAt);
