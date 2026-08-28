using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for deterministic, template-based narration (roadmap §5.B, I4 — "Narrate").
/// Like <see cref="CorrelationFindingsController"/>, detection here is inherently cross-namespace
/// — a narration can stitch together findings from every namespace the caller can access, plus
/// any correlation spanning them — so this controller runs over every accessible namespace rather
/// than a single <c>namespaceId</c>.
/// </summary>
[Route(ApiRoutes.Narrations.Base)]
[Tags("Narrations")]
public sealed class NarrationsController : ApiControllerBase
{
    private readonly IAnomalyDetectionService _anomalyDetectionService;
    private readonly IDriftDetectionService _driftDetectionService;
    private readonly ICorrelationDetectionService _correlationDetectionService;
    private readonly INarrationService _narrationService;
    private readonly INarrationResultCache _narrationResultCache;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<NarrationsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="NarrationsController"/> class.
    /// </summary>
    public NarrationsController(
        IAnomalyDetectionService anomalyDetectionService,
        IDriftDetectionService driftDetectionService,
        ICorrelationDetectionService correlationDetectionService,
        INarrationService narrationService,
        INarrationResultCache narrationResultCache,
        INamespaceRepository namespaceRepository,
        ILogger<NarrationsController> logger)
    {
        _anomalyDetectionService = anomalyDetectionService ?? throw new ArgumentNullException(nameof(anomalyDetectionService));
        _driftDetectionService = driftDetectionService ?? throw new ArgumentNullException(nameof(driftDetectionService));
        _correlationDetectionService = correlationDetectionService ?? throw new ArgumentNullException(nameof(correlationDetectionService));
        _narrationService = narrationService ?? throw new ArgumentNullException(nameof(narrationService));
        _narrationResultCache = narrationResultCache ?? throw new ArgumentNullException(nameof(narrationResultCache));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Generates narrations across every namespace the caller can access, within a specified
    /// time window: one per namespace with any anomaly or drift finding, plus one per
    /// same-provider correlation.
    /// </summary>
    /// <param name="startTime">The start of the analysis window (defaults to 24 hours ago).</param>
    /// <param name="endTime">The end of the analysis window (defaults to now).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The generated narrations.</returns>
    /// <response code="200">Narrations generated successfully.</response>
    /// <response code="400">endTime was not after startTime.</response>
    [RequireScope(ApiKeyScopes.NarrationsRead)]
    [HttpPost("generate")]
    [ProducesResponseType(typeof(NarrationGenerationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<NarrationGenerationResponse>> Generate(
        [FromQuery] DateTimeOffset? startTime = null,
        [FromQuery] DateTimeOffset? endTime = null,
        CancellationToken cancellationToken = default)
    {
        var start = startTime ?? DateTimeOffset.UtcNow.AddHours(-24);
        var end = endTime ?? DateTimeOffset.UtcNow;

        if (end <= start)
        {
            return ToActionResult<NarrationGenerationResponse>(ServiceHub.Shared.Results.Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "endTime must be after startTime."));
        }

        var namespacesResult = await _namespaceRepository.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        if (namespacesResult.IsFailure)
        {
            return ToActionResult<NarrationGenerationResponse>(namespacesResult.Error);
        }

        var namespaces = namespacesResult.Value.Where(n => n.IsActive).ToList();
        var namespacesById = namespaces.ToDictionary(n => n.Id);

        var anomalies = new List<Anomaly>();
        var driftFindings = new List<DriftFinding>();
        var observations = new List<AnomalyObservation>();

        foreach (var ns in namespaces)
        {
            var anomalyResult = await _anomalyDetectionService
                .DetectAnomaliesAsync(ns.Id, start, end, cancellationToken)
                .ConfigureAwait(false);

            if (anomalyResult.IsFailure)
            {
                _logger.LogWarning(
                    "Anomaly detection failed for namespace {NamespaceId} during narration generate: {Error}",
                    ns.Id,
                    anomalyResult.Error.Message);
            }
            else
            {
                anomalies.AddRange(anomalyResult.Value);
                observations.AddRange(anomalyResult.Value.Select(a => new AnomalyObservation(a, ns.OwnerId, ns.Provider)));
            }

            var driftResult = await _driftDetectionService
                .DetectDriftAsync(ns.Id, start, end, cancellationToken)
                .ConfigureAwait(false);

            if (driftResult.IsFailure)
            {
                _logger.LogWarning(
                    "Drift detection failed for namespace {NamespaceId} during narration generate: {Error}",
                    ns.Id,
                    driftResult.Error.Message);
            }
            else
            {
                driftFindings.AddRange(driftResult.Value);
            }
        }

        var correlationFindings = _correlationDetectionService.DetectCorrelations(observations);

        var narrations = _narrationService.GenerateNarrations(namespacesById, anomalies, driftFindings, correlationFindings);

        // Cache the results so a subsequent GET /{id} can retrieve one of them (see
        // INarrationResultCache for why this isn't backed by the database).
        _narrationResultCache.Store(narrations);

        var narrationInfos = narrations.Select(MapToNarrationInfo).ToList();

        _logger.LogInformation(
            "Generated {NarrationCount} narration(s) for owner {OwnerId} across {NamespaceCount} namespace(s)",
            narrationInfos.Count,
            OwnerId,
            namespaces.Count);

        return Ok(new NarrationGenerationResponse(
            StartTime: start,
            EndTime: end,
            Narrations: narrationInfos,
            GeneratedAt: DateTimeOffset.UtcNow));
    }

    /// <summary>
    /// Gets a specific narration by ID.
    /// </summary>
    /// <param name="id">The narration ID.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The narration details.</returns>
    /// <response code="200">Narration retrieved successfully.</response>
    /// <response code="404">Narration not found.</response>
    [RequireScope(ApiKeyScopes.NarrationsRead)]
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(NarrationInfo), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<NarrationInfo>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Getting narration {NarrationId}", id);

        var narration = _narrationResultCache.TryGet(id);
        if (narration is null)
        {
            return NotFoundResult(id);
        }

        // TENANT ISOLATION: every namespace this narration draws from must be accessible to the
        // caller — a cross-namespace correlation narration can combine namespaces the caller only
        // reaches via sharing, and if even one contributor isn't accessible, exposing the
        // narration would leak that namespace's existence.
        foreach (var namespaceId in narration.AccessNamespaceIds)
        {
            var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
            if (namespaceResult.IsFailure || !namespaceResult.Value.IsAccessibleBy(OwnerId, AllowedNamespaceIds))
            {
                return NotFoundResult(id);
            }
        }

        return Ok(MapToNarrationInfo(narration));
    }

    private ActionResult<NarrationInfo> NotFoundResult(Guid id) =>
        ToActionResult<NarrationInfo>(ServiceHub.Shared.Results.Error.NotFound(
            "Narration.NotFound",
            $"Narration with ID '{id}' was not found."));

    private static NarrationInfo MapToNarrationInfo(Narration narration)
    {
        return new NarrationInfo(
            Id: narration.Id,
            Kind: narration.Kind.ToString(),
            NamespaceId: narration.NamespaceId,
            Headline: narration.Headline,
            Summary: narration.Summary,
            Severity: narration.Severity,
            GeneratedAt: narration.GeneratedAt,
            RecommendedActions: narration.RecommendedActions);
    }
}

/// <summary>
/// Information about a generated narration.
/// </summary>
public sealed record NarrationInfo(
    Guid Id,
    string Kind,
    Guid? NamespaceId,
    string Headline,
    string Summary,
    int Severity,
    DateTimeOffset GeneratedAt,
    IReadOnlyList<string> RecommendedActions);

/// <summary>
/// Response model for narration generation results.
/// </summary>
public sealed record NarrationGenerationResponse(
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    IReadOnlyList<NarrationInfo> Narrations,
    DateTimeOffset GeneratedAt);
