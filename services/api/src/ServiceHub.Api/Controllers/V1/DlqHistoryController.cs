using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using ServiceHub.Api.Authorization;
using ServiceHub.Api.Filters;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Helpers;
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for DLQ Intelligence operations.
/// Provides historical tracking, timeline views, and export capabilities.
/// </summary>
[Route(ApiRoutes.Dlq.Base)]
[Tags("DLQ Intelligence")]
public sealed class DlqHistoryController : ApiControllerBase
{
    private static readonly TimeSpan SignatureCacheDuration = TimeSpan.FromSeconds(60);

    private readonly IDlqHistoryService _historyService;
    private readonly ILogger<DlqHistoryController> _logger;
    private readonly IDlqSignatureAnalysisService _signatureAnalysisService;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IMemoryCache _cache;
    private readonly IFailureKnowledgeService _knowledgeService;
    private readonly INamespaceSignatureLookupService _signatureLookupService;
    private readonly ISignatureLifecycleService _lifecycleService;
    private readonly ISignatureReplayService _signatureReplayService;

    /// <summary>
    /// Initializes a new instance of the <see cref="DlqHistoryController"/> class.
    /// </summary>
    public DlqHistoryController(
        IDlqHistoryService historyService,
        ILogger<DlqHistoryController> logger,
        IDlqSignatureAnalysisService signatureAnalysisService,
        INamespaceRepository namespaceRepository,
        IMemoryCache cache,
        IFailureKnowledgeService knowledgeService,
        INamespaceSignatureLookupService signatureLookupService,
        ISignatureLifecycleService lifecycleService,
        ISignatureReplayService signatureReplayService)
    {
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _signatureAnalysisService = signatureAnalysisService ?? throw new ArgumentNullException(nameof(signatureAnalysisService));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _knowledgeService = knowledgeService ?? throw new ArgumentNullException(nameof(knowledgeService));
        _signatureLookupService = signatureLookupService ?? throw new ArgumentNullException(nameof(signatureLookupService));
        _lifecycleService = lifecycleService ?? throw new ArgumentNullException(nameof(lifecycleService));
        _signatureReplayService = signatureReplayService ?? throw new ArgumentNullException(nameof(signatureReplayService));
    }

    /// <summary>
    /// Gets paginated DLQ message history with optional filters.
    /// </summary>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="entityName">Optional entity name filter.</param>
    /// <param name="from">Optional start date filter.</param>
    /// <param name="to">Optional end date filter.</param>
    /// <param name="status">Optional status filter (Active, Replayed, Archived, Discarded).</param>
    /// <param name="category">Optional failure category filter.</param>
    /// <param name="page">Page number (default: 1).</param>
    /// <param name="pageSize">Items per page (default: 50, max: 200).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of DLQ messages.</returns>
    [HttpGet("history")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(PaginatedResponse<DlqHistoryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PaginatedResponse<DlqHistoryResponse>>> GetHistory(
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] string? entityName = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] DlqMessageStatus? status = null,
        [FromQuery] FailureCategory? category = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var result = await _historyService.GetHistoryAsync(
            OwnerId, namespaceId, entityName, from, to, status, category,
            page, pageSize, cancellationToken);

        if (result.IsFailure)
            return ToActionResult<PaginatedResponse<DlqHistoryResponse>>(result.Error);

        var data = result.Value;
        var items = data.Items.Select(MapToResponse).ToList();

        var response = new PaginatedResponse<DlqHistoryResponse>(
            Items: items,
            TotalCount: data.TotalCount,
            Page: data.Page,
            PageSize: data.PageSize,
            HasNextPage: data.HasNextPage,
            HasPreviousPage: data.HasPreviousPage);

        return Ok(response);
    }

    /// <summary>
    /// Gets a single DLQ message with full details including replay history.
    /// </summary>
    /// <param name="id">The DLQ message ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Full DLQ message details.</returns>
    [HttpGet("history/{id:long}")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(DlqMessageDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DlqMessageDetailResponse>> GetById(
        long id,
        CancellationToken cancellationToken = default)
    {
        var result = await _historyService.GetByIdAsync(OwnerId, id, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<DlqMessageDetailResponse>(result.Error);

        var m = result.Value;
        var response = new DlqMessageDetailResponse(
            Id: m.Id,
            MessageId: m.MessageId,
            SequenceNumber: m.SequenceNumber,
            BodyHash: m.BodyHash,
            NamespaceId: m.NamespaceId,
            CloudProvider: m.CloudProvider.ToString().ToLowerInvariant(),
            EntityName: m.EntityName,
            EntityType: m.EntityType.ToString(),
            EnqueuedTimeUtc: m.EnqueuedTimeUtc,
            DeadLetterTimeUtc: m.DeadLetterTimeUtc,
            DetectedAtUtc: m.DetectedAtUtc,
            DeadLetterReason: m.DeadLetterReason,
            DeadLetterErrorDescription: m.DeadLetterErrorDescription,
            DeliveryCount: m.DeliveryCount,
            ContentType: m.ContentType,
            MessageSize: m.MessageSize,
            BodyPreview: m.BodyPreview,
            ApplicationPropertiesJson: m.ApplicationPropertiesJson,
            FailureCategory: m.FailureCategory.ToString(),
            CategoryConfidence: m.CategoryConfidence,
            Status: m.Status.ToString(),
            ReplayedAt: m.ReplayedAt,
            ReplaySuccess: m.ReplaySuccess,
            ArchivedAt: m.ArchivedAt,
            ResolvedAt: m.ResolvedAt,
            UserNotes: m.UserNotes,
            CorrelationId: m.CorrelationId,
            SessionId: m.SessionId,
            TopicName: m.TopicName,
            ReplayHistory: m.ReplayHistories.Select(r => new ReplayHistoryResponse(
                Id: r.Id,
                ReplayedAt: r.ReplayedAt,
                ReplayedBy: r.ReplayedBy,
                ReplayStrategy: r.ReplayStrategy,
                ReplayedToEntity: r.ReplayedToEntity,
                OutcomeStatus: r.OutcomeStatus,
                NewDeadLetterReason: r.NewDeadLetterReason,
                ErrorDetails: r.ErrorDetails
            )).ToList());

        return Ok(response);
    }

    /// <summary>
    /// Gets the timeline (lifecycle events) for a specific DLQ message.
    /// </summary>
    /// <param name="id">The DLQ message ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Timeline of message lifecycle events.</returns>
    [HttpGet("history/{id:long}/timeline")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(DlqTimelineResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DlqTimelineResponse>> GetTimeline(
        long id,
        CancellationToken cancellationToken = default)
    {
        var result = await _historyService.GetTimelineAsync(OwnerId, id, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<DlqTimelineResponse>(result.Error);

        var events = result.Value.Select(e => new DlqTimelineEventResponse(
            EventType: e.EventType,
            Description: e.Description,
            Timestamp: e.Timestamp,
            Details: e.Details
        )).ToList();

        var response = new DlqTimelineResponse(
            MessageId: id,
            EntityName: string.Empty,
            Events: events);

        return Ok(response);
    }

    /// <summary>
    /// Updates the user notes on a DLQ message.
    /// </summary>
    /// <param name="id">The DLQ message ID.</param>
    /// <param name="request">The notes update request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated DLQ message.</returns>
    [HttpPost("history/{id:long}/notes")]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(DlqHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DlqHistoryResponse>> UpdateNotes(
        long id,
        [FromBody] UpdateDlqNotesRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _historyService.UpdateNotesAsync(OwnerId, id, request.Notes, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<DlqHistoryResponse>(result.Error);

        return Ok(MapToResponse(result.Value));
    }

    /// <summary>
    /// Triages a DLQ message by transitioning its lifecycle status (Active, Archived,
    /// Discarded, Resolved) — the action that turns the DLQ history into a triage inbox.
    /// </summary>
    /// <param name="id">The DLQ message ID.</param>
    /// <param name="request">The status transition request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated DLQ message.</returns>
    [HttpPost("history/{id:long}/status")]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(DlqHistoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DlqHistoryResponse>> UpdateStatus(
        long id,
        [FromBody] UpdateDlqStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await _historyService.UpdateStatusAsync(
            OwnerId, id, request.Status, request.Notes, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<DlqHistoryResponse>(result.Error);

        return Ok(MapToResponse(result.Value));
    }

    /// <summary>Maps a persisted DLQ message to its API response shape.</summary>
    private static DlqHistoryResponse MapToResponse(Core.Entities.DlqMessage m) => new(
        Id: m.Id,
        MessageId: m.MessageId,
        SequenceNumber: m.SequenceNumber,
        BodyHash: m.BodyHash,
        NamespaceId: m.NamespaceId,
        CloudProvider: m.CloudProvider.ToString().ToLowerInvariant(),
        EntityName: m.EntityName,
        EntityType: m.EntityType.ToString(),
        EnqueuedTimeUtc: m.EnqueuedTimeUtc,
        DeadLetterTimeUtc: m.DeadLetterTimeUtc,
        DetectedAtUtc: m.DetectedAtUtc,
        DeadLetterReason: m.DeadLetterReason,
        DeadLetterErrorDescription: m.DeadLetterErrorDescription,
        DeliveryCount: m.DeliveryCount,
        ContentType: m.ContentType,
        MessageSize: m.MessageSize,
        BodyPreview: m.BodyPreview,
        FailureCategory: m.FailureCategory.ToString(),
        CategoryConfidence: m.CategoryConfidence,
        Status: m.Status.ToString(),
        ReplayedAt: m.ReplayedAt,
        ReplaySuccess: m.ReplaySuccess,
        ArchivedAt: m.ArchivedAt,
        ResolvedAt: m.ResolvedAt,
        UserNotes: m.UserNotes,
        CorrelationId: m.CorrelationId,
        TopicName: m.TopicName,
        ForensicRootCause: m.ForensicRootCause,
        ForensicConfidence: m.ForensicConfidence,
        ReplaySafety: m.ReplaySafety);

    /// <summary>
    /// Exports DLQ messages in the specified format (JSON or CSV).
    /// </summary>
    /// <param name="format">Export format: json or csv (default: json).</param>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="entityName">Optional entity name filter.</param>
    /// <param name="from">Optional start date filter.</param>
    /// <param name="to">Optional end date filter.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>File download with DLQ message data.</returns>
    [HttpGet("export")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(
        [FromQuery] string format = "json",
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] string? entityName = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] DlqMessageStatus? status = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _historyService.ExportAsync(
            OwnerId, namespaceId, entityName, from, to, status, cancellationToken);

        if (result.IsFailure)
            return ToActionResult(ServiceHub.Shared.Results.Result.Failure(result.Error));

        var messages = result.Value;

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = GenerateCsv(messages);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "dlq-export.csv");
        }

        var jsonItems = messages.Select(MapToResponse).ToList();

        var json = JsonSerializer.Serialize(jsonItems, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return File(Encoding.UTF8.GetBytes(json), "application/json", "dlq-export.json");
    }

    /// <summary>
    /// Gets a summary of DLQ activity across all or a specific namespace.
    /// </summary>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="days">Number of days for the daily trend (1–365, default 30).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>DLQ summary statistics.</returns>
    [HttpGet("summary")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(DlqSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DlqSummaryResponse>> GetSummary(
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] int days = 30,
        CancellationToken cancellationToken = default)
    {
        var result = await _historyService.GetSummaryAsync(OwnerId, namespaceId, days, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<DlqSummaryResponse>(result.Error);

        var s = result.Value;
        var response = new DlqSummaryResponse(
            TotalMessages: s.TotalMessages,
            ActiveMessages: s.ActiveMessages,
            ReplayedMessages: s.ReplayedMessages,
            ArchivedMessages: s.ArchivedMessages,
            ByCategory: s.ByCategory,
            ByEntity: s.ByEntity,
            OldestMessage: s.OldestMessage,
            NewestMessage: s.NewestMessage,
            DailyTrend: s.DailyTrend.Select(t => new DlqTrendPointResponse(
                Date: t.Date,
                NewMessages: t.NewMessages,
                ResolvedMessages: t.ResolvedMessages
            )).ToList());

        return Ok(response);
    }

    /// <summary>
    /// Gets a 7-day DLQ trend for a specific namespace.
    /// Returns daily new and resolved message counts.
    /// </summary>
    /// <param name="namespaceId">The namespace to get trend data for.</param>
    /// <param name="days">Number of days (1–30, default 7).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Array of daily trend data points.</returns>
    [HttpGet("trend")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(IReadOnlyList<DlqTrendPointResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DlqTrendPointResponse>>> GetTrend(
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        days = Math.Clamp(days, 1, 30);
        var result = await _historyService.GetSummaryAsync(OwnerId, namespaceId, days, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<IReadOnlyList<DlqTrendPointResponse>>(result.Error);

        var trend = result.Value.DailyTrend.Select(t => new DlqTrendPointResponse(
            Date: t.Date,
            NewMessages: t.NewMessages,
            ResolvedMessages: t.ResolvedMessages
        )).ToList();

        return Ok(trend);
    }

    /// <summary>
    /// Gets a namespace's DLQ error-cluster signatures: identity, new-vs-recurring history, and
    /// a human-readable explanation per cluster. When the AI service is unavailable, returns 200
    /// with <c>available: false</c> so the frontend renders its unavailable state rather than an
    /// error page. Cached per namespace for 60 seconds so repeated page loads do not re-run
    /// clustering.
    /// </summary>
    /// <param name="namespaceId">The namespace to analyze.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The namespace's DLQ signature analysis.</returns>
    [HttpGet("~/" + ApiRoutes.Dlq.Signatures)]
    [RequireNamespaceOwnership]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(DlqSignaturesResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DlqSignaturesResponse>> GetSignatures(
        Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        var result = await GetOrBuildSignaturesResponseAsync(namespaceId, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Ownership-checks, cache-or-computes, and returns a namespace's signature analysis —
    /// shared by <see cref="GetSignatures"/> and <see cref="GetSignatureDetail"/> so both use
    /// the exact same 60s-cached clustering pass instead of re-running analysis.
    /// </summary>
    private async Task<Result<DlqSignaturesResponse>> GetOrBuildSignaturesResponseAsync(
        Guid namespaceId, CancellationToken cancellationToken)
    {
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
            return Result<DlqSignaturesResponse>.Failure(namespaceResult.Error);

        var cacheKey = $"dlq-signatures:{OwnerId}:{namespaceId}";
        if (_cache.TryGetValue(cacheKey, out DlqSignaturesResponse? cached) && cached is not null)
            return cached;

        var result = await _signatureAnalysisService.AnalyzeAsync(OwnerId, namespaceId, cancellationToken);
        if (result.IsFailure)
            return Result<DlqSignaturesResponse>.Failure(result.Error);

        var response = await MapToSignaturesResponseAsync(result.Value, OwnerId, namespaceId, cancellationToken);
        _cache.Set(cacheKey, response, SignatureCacheDuration);

        return response;
    }

    /// <summary>Maps a composed signature analysis to its API response shape, including operational knowledge.</summary>
    private async Task<DlqSignaturesResponse> MapToSignaturesResponseAsync(
        DlqSignatureAnalysisResult analysis,
        string ownerId,
        Guid namespaceId,
        CancellationToken cancellationToken)
    {
        // Load knowledge for all clusters in a single batch operation
        var clusterHashes = analysis.Clusters.Select(c => c.TopTerms).ToList();
        if (clusterHashes.Count == 0)
        {
            return new DlqSignaturesResponse(
                Available: analysis.Available,
                Method: analysis.Method,
                BatchSize: analysis.BatchSize,
                Clusters: [],
                Singletons: analysis.Singletons.Select(s => new DlqSingletonSignatureResponse(
                    MessageId: s.MessageId,
                    DominantEntity: s.DominantEntity,
                    DominantDeadletterReason: s.DominantDeadletterReason)).ToList());
        }

        var clusterSignatureHashes = analysis.Clusters
            .Select(c => ClusterSignatureHasher.ComputeHash(c.TopTerms, c.DominantDeadletterReason))
            .ToList();

        var knowledgeResult = await _knowledgeService.GetKnowledgeBatchAsync(
            ownerId, namespaceId, clusterSignatureHashes, cancellationToken)
            .ConfigureAwait(false);

        var knowledgeByHash = knowledgeResult.IsSuccess
            ? knowledgeResult.Value
            : new Dictionary<string, Core.Models.FailureKnowledge>();

        var statusResult = await _lifecycleService.GetStatusBatchAsync(
            ownerId, namespaceId, clusterSignatureHashes, cancellationToken)
            .ConfigureAwait(false);

        var statusByHash = statusResult.IsSuccess
            ? statusResult.Value
            : new Dictionary<string, Core.Models.SignatureLifecycleSnapshot>();

        var now = DateTimeOffset.UtcNow;

        return new DlqSignaturesResponse(
            Available: analysis.Available,
            Method: analysis.Method,
            BatchSize: analysis.BatchSize,
            Clusters: analysis.Clusters.Select((c, idx) =>
            {
                var hash = clusterSignatureHashes[idx];
                var knowledge = knowledgeByHash.TryGetValue(hash, out var k) ? k : null;
                var status = statusByHash.TryGetValue(hash, out var snapshot)
                    ? snapshot.Status
                    : SignatureLifecycleStatus.Active;
                var trend = Shared.Helpers.SignatureTrendHeuristic.Compute(
                    c.IsNew, c.OccurrenceCount, c.FirstSeenAt, c.WindowEnd, now);

                return new DlqClusterSignatureResponse(
                    Size: c.Size,
                    MessageIds: c.MessageIds,
                    DominantEntity: c.DominantEntity,
                    DominantDeadletterReason: c.DominantDeadletterReason,
                    DominantDeadletterReasonCount: c.DominantDeadletterReasonCount,
                    TopTerms: c.TopTerms,
                    IsNew: c.IsNew,
                    FirstSeenAt: c.FirstSeenAt,
                    OccurrenceCount: c.OccurrenceCount,
                    WindowStart: c.WindowStart,
                    WindowEnd: c.WindowEnd,
                    Explanation: c.Explanation,
                    Knowledge: ToKnowledgeResponse(knowledge),
                    SignatureHash: hash,
                    Status: status.ToString(),
                    Trend: trend);
            }).ToList(),
            Singletons: analysis.Singletons.Select(s => new DlqSingletonSignatureResponse(
                MessageId: s.MessageId,
                DominantEntity: s.DominantEntity,
                DominantDeadletterReason: s.DominantDeadletterReason)).ToList());
    }

    /// <summary>Maps operational knowledge to its API response shape, or null if none exists.</summary>
    private static KnowledgeResponse? ToKnowledgeResponse(Core.Models.FailureKnowledge? knowledge) =>
        knowledge is null ? null : new KnowledgeResponse(
            RootCause: knowledge.RootCause,
            ResolutionNotes: knowledge.ResolutionNotes,
            OperationalNotes: knowledge.OperationalNotes,
            RunbookLink: knowledge.RunbookLink,
            Owner: knowledge.Owner,
            ReplayGuidance: knowledge.ReplayGuidance,
            LastUpdatedAt: knowledge.LastUpdatedAt,
            KnowledgeVersion: knowledge.KnowledgeVersion,
            ReviewDueAt: knowledge.ReviewDueAt,
            Tags: knowledge.Tags,
            UpdatedBy: knowledge.UpdatedBy,
            IsReviewOverdue: knowledge.ReviewDueAt.HasValue && knowledge.ReviewDueAt.Value < DateTimeOffset.UtcNow);

    /// <summary>Derives a signature's confidence label from the clustering method used, mirroring
    /// the High/AI vs. Medium/deterministic heuristic in DlqSignatureAnalysisService.</summary>
    private static string DeriveConfidence(string? method) =>
        string.Equals(method, "clustered", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(method, "grouped", StringComparison.OrdinalIgnoreCase)
            ? "High"
            : "Medium";

    /// <summary>
    /// Gets full detail for a single failure signature: identity, history, knowledge, lifecycle
    /// status/trend, and its related DLQ messages. Falls back to the persisted historical record
    /// (NamespaceSignature) when the signature's messages are no longer in the current cluster set.
    /// </summary>
    /// <param name="namespaceId">The namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's stable identity hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("~/" + ApiRoutes.Dlq.SignatureById)]
    [RequireNamespaceOwnership]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(DlqSignatureDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DlqSignatureDetailResponse>> GetSignatureDetail(
        Guid namespaceId,
        string signatureHash,
        CancellationToken cancellationToken = default)
    {
        var signaturesResult = await GetOrBuildSignaturesResponseAsync(namespaceId, cancellationToken);
        if (signaturesResult.IsFailure)
            return ToActionResult<DlqSignatureDetailResponse>(signaturesResult.Error);

        var cluster = signaturesResult.Value.Clusters.FirstOrDefault(c => c.SignatureHash == signatureHash);
        if (cluster is not null)
        {
            var relatedResult = await _historyService.GetByIdsAsync(OwnerId, cluster.MessageIds, cancellationToken);
            var relatedMessages = relatedResult.IsSuccess
                ? relatedResult.Value.Select(MapToResponse).ToList()
                : [];

            return Ok(new DlqSignatureDetailResponse(
                SignatureHash: signatureHash,
                NamespaceId: namespaceId,
                Size: cluster.Size,
                MessageIds: cluster.MessageIds,
                DominantEntity: cluster.DominantEntity,
                DominantDeadletterReason: cluster.DominantDeadletterReason,
                DominantDeadletterReasonCount: cluster.DominantDeadletterReasonCount,
                TopTerms: cluster.TopTerms,
                IsNew: cluster.IsNew,
                FirstSeenAt: cluster.FirstSeenAt,
                OccurrenceCount: cluster.OccurrenceCount,
                WindowStart: cluster.WindowStart,
                WindowEnd: cluster.WindowEnd,
                Explanation: cluster.Explanation,
                Knowledge: cluster.Knowledge,
                Status: cluster.Status,
                Trend: cluster.Trend,
                Confidence: DeriveConfidence(signaturesResult.Value.Method),
                IsCurrentlyClustered: true,
                RelatedMessages: relatedMessages));
        }

        // Not currently clustered — fall back to the persisted historical record, if any.
        var persisted = await _signatureLookupService.GetByHashAsync(
            OwnerId, namespaceId, signatureHash, cancellationToken);
        if (persisted is null)
        {
            return ToActionResult<DlqSignatureDetailResponse>(Error.NotFound(
                "Dlq.SignatureNotFound", $"Failure signature '{signatureHash}' was not found."));
        }

        var knowledgeResult = await _knowledgeService.GetKnowledgeAsync(
            OwnerId, namespaceId, signatureHash, cancellationToken);
        var knowledgeResponse = knowledgeResult.IsSuccess ? ToKnowledgeResponse(knowledgeResult.Value) : null;

        var statusResult = await _lifecycleService.GetStatusAsync(
            OwnerId, namespaceId, signatureHash, cancellationToken);
        var status = statusResult.IsSuccess ? statusResult.Value.Status : SignatureLifecycleStatus.Active;

        var topTerms = JsonSerializer.Deserialize<List<string>>(persisted.TopTermsJson) ?? [];
        var trend = Shared.Helpers.SignatureTrendHeuristic.Compute(
            isNew: false, persisted.OccurrenceCount, persisted.FirstSeenAt, persisted.LastSeenAt, DateTimeOffset.UtcNow);

        return Ok(new DlqSignatureDetailResponse(
            SignatureHash: signatureHash,
            NamespaceId: namespaceId,
            Size: 0,
            MessageIds: [],
            DominantEntity: string.Empty,
            DominantDeadletterReason: persisted.DominantDeadletterReason,
            DominantDeadletterReasonCount: 0,
            TopTerms: topTerms,
            IsNew: false,
            FirstSeenAt: persisted.FirstSeenAt,
            OccurrenceCount: persisted.OccurrenceCount,
            WindowStart: persisted.FirstSeenAt,
            WindowEnd: persisted.LastSeenAt,
            Explanation: "This signature's messages are no longer active in the DLQ — showing its historical record only.",
            Knowledge: knowledgeResponse,
            Status: status.ToString(),
            Trend: trend,
            Confidence: "Medium",
            IsCurrentlyClustered: false,
            RelatedMessages: []));
    }

    /// <summary>
    /// Gets the merged, computed lifecycle timeline for a failure signature: first observed,
    /// recurrences, knowledge recorded, and lifecycle status transitions, sorted ascending.
    /// </summary>
    /// <param name="namespaceId">The namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's stable identity hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("~/" + ApiRoutes.Dlq.SignatureTimeline)]
    [RequireNamespaceOwnership]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(SignatureTimelineResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SignatureTimelineResponse>> GetSignatureTimeline(
        Guid namespaceId,
        string signatureHash,
        CancellationToken cancellationToken = default)
    {
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
            return ToActionResult<SignatureTimelineResponse>(namespaceResult.Error);

        var persisted = await _signatureLookupService.GetByHashAsync(
            OwnerId, namespaceId, signatureHash, cancellationToken);

        var historyResult = await _lifecycleService.GetHistoryAsync(
            OwnerId, namespaceId, signatureHash, cancellationToken);
        var lifecycleEvents = historyResult.IsSuccess ? historyResult.Value : [];

        if (persisted is null && lifecycleEvents.Count == 0)
        {
            return ToActionResult<SignatureTimelineResponse>(Error.NotFound(
                "Dlq.SignatureNotFound", $"Failure signature '{signatureHash}' was not found."));
        }

        var events = new List<DlqTimelineEventResponse>();

        if (persisted is not null)
        {
            events.Add(new DlqTimelineEventResponse(
                EventType: "SignatureFirstObserved",
                Description: "Signature first observed in this namespace's DLQ",
                Timestamp: persisted.FirstSeenAt,
                Details: null));

            if (persisted.LastSeenAt > persisted.FirstSeenAt)
            {
                events.Add(new DlqTimelineEventResponse(
                    EventType: "SignatureRecurred",
                    Description: $"Signature recurred (seen {persisted.OccurrenceCount} times total)",
                    Timestamp: persisted.LastSeenAt,
                    Details: new Dictionary<string, string> { ["OccurrenceCount"] = persisted.OccurrenceCount.ToString() }));
            }
        }

        var knowledgeResult = await _knowledgeService.GetKnowledgeAsync(
            OwnerId, namespaceId, signatureHash, cancellationToken);
        if (knowledgeResult.IsSuccess && knowledgeResult.Value.LastUpdatedAt.HasValue)
        {
            events.Add(new DlqTimelineEventResponse(
                EventType: "KnowledgeRecorded",
                Description: "Operational knowledge recorded for this signature",
                Timestamp: knowledgeResult.Value.LastUpdatedAt.Value,
                Details: null));
        }

        foreach (var e in lifecycleEvents)
        {
            events.Add(new DlqTimelineEventResponse(
                EventType: "StatusChanged",
                Description: $"Status changed from {e.FromStatus} to {e.ToStatus}",
                Timestamp: e.Timestamp,
                Details: new Dictionary<string, string>
                {
                    ["From"] = e.FromStatus.ToString(),
                    ["To"] = e.ToStatus.ToString(),
                    ["Notes"] = e.Notes ?? string.Empty,
                }));
        }

        var replayJobsResult = await _signatureReplayService.ListJobsAsync(
            OwnerId, namespaceId, signatureHash, page: 1, pageSize: 100, cancellationToken);
        if (replayJobsResult.IsSuccess)
        {
            foreach (var job in replayJobsResult.Value.Items)
                events.AddRange(BuildReplayJobEvents(job));
        }

        var sorted = events.OrderBy(e => e.Timestamp).ToList();
        return Ok(new SignatureTimelineResponse(signatureHash, sorted));
    }

    /// <summary>Maps one signature-replay job's lifecycle into timeline events (started, plus a
    /// terminal completed/failed/cancelled event once the job has finished).</summary>
    private static IEnumerable<DlqTimelineEventResponse> BuildReplayJobEvents(BulkOperationJobResponse job)
    {
        yield return new DlqTimelineEventResponse(
            EventType: "ReplayJobStarted",
            Description: $"Signature replay started ({job.TotalMatched} message(s) matched)",
            Timestamp: job.CreatedAt,
            Details: new Dictionary<string, string> { ["TotalMatched"] = job.TotalMatched.ToString() });

        if (job.CompletedAt is null)
            yield break;

        var details = new Dictionary<string, string>
        {
            ["SuccessCount"] = job.SuccessCount.ToString(),
            ["FailureCount"] = job.FailureCount.ToString(),
            ["TotalMatched"] = job.TotalMatched.ToString(),
        };

        switch (job.Status)
        {
            case "Completed":
                yield return new DlqTimelineEventResponse(
                    EventType: "ReplayJobCompleted",
                    Description: $"Signature replay completed — {job.SuccessCount}/{job.TotalMatched} succeeded",
                    Timestamp: job.CompletedAt.Value,
                    Details: details);
                break;
            case "CompletedWithErrors":
                yield return new DlqTimelineEventResponse(
                    EventType: "ReplayJobCompleted",
                    Description: $"Signature replay completed with errors — {job.SuccessCount} succeeded, {job.FailureCount} failed",
                    Timestamp: job.CompletedAt.Value,
                    Details: details);
                break;
            case "Failed":
                yield return new DlqTimelineEventResponse(
                    EventType: "ReplayJobFailed",
                    Description: string.IsNullOrEmpty(job.ErrorSummary)
                        ? "Signature replay failed"
                        : $"Signature replay failed: {job.ErrorSummary}",
                    Timestamp: job.CompletedAt.Value,
                    Details: details);
                break;
            case "Cancelled":
                yield return new DlqTimelineEventResponse(
                    EventType: "ReplayJobCancelled",
                    Description: $"Signature replay cancelled after {job.ProcessedCount}/{job.TotalMatched} message(s)",
                    Timestamp: job.CompletedAt.Value,
                    Details: details);
                break;
        }
    }

    /// <summary>
    /// Transitions a failure signature's lifecycle status (Resolved/Reopened/Suppressed/Archived).
    /// Invalidates the cached signature list so its Status doesn't serve stale for up to 60s.
    /// </summary>
    /// <param name="namespaceId">The namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's stable identity hash.</param>
    /// <param name="request">The target status and optional notes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("~/" + ApiRoutes.Dlq.SignatureStatus)]
    [RequireNamespaceOwnership]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(SignatureLifecycleStatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SignatureLifecycleStatusResponse>> UpdateSignatureStatus(
        Guid namespaceId,
        string signatureHash,
        [FromBody] UpdateSignatureStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
            return ToActionResult<SignatureLifecycleStatusResponse>(namespaceResult.Error);

        var result = await _lifecycleService.TransitionAsync(
            OwnerId, namespaceId, signatureHash, request.Status, request.Notes, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<SignatureLifecycleStatusResponse>(result.Error);

        _cache.Remove($"dlq-signatures:{OwnerId}:{namespaceId}");

        var snapshot = result.Value;
        return Ok(new SignatureLifecycleStatusResponse(
            SignatureHash: signatureHash,
            Status: snapshot.Status.ToString(),
            PreviousStatus: snapshot.PreviousStatus?.ToString(),
            TransitionedAt: snapshot.TransitionedAt,
            Notes: snapshot.Notes));
    }

    /// <summary>
    /// Creates or updates a failure signature's operational knowledge. On update, the prior
    /// version is snapshotted into history before being overwritten. Invalidates the cached
    /// signature list so the change is reflected immediately rather than for up to 60s.
    /// </summary>
    /// <param name="namespaceId">The namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's stable identity hash.</param>
    /// <param name="request">The knowledge fields to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPut("~/" + ApiRoutes.Dlq.SignatureKnowledge)]
    [RequireNamespaceOwnership]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(KnowledgeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KnowledgeResponse>> UpsertKnowledge(
        Guid namespaceId,
        string signatureHash,
        [FromBody] UpsertKnowledgeRequest request,
        CancellationToken cancellationToken = default)
    {
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
            return ToActionResult<KnowledgeResponse>(namespaceResult.Error);

        var knowledge = new Core.Models.FailureKnowledge(
            RootCause: request.RootCause,
            ResolutionNotes: request.ResolutionNotes,
            OperationalNotes: request.OperationalNotes,
            RunbookLink: request.RunbookLink,
            Owner: request.Owner,
            ReplayGuidance: request.ReplayGuidance,
            LastUpdatedAt: null,
            KnowledgeVersion: 1,
            ReviewDueAt: request.ReviewDueAt,
            Tags: request.Tags,
            UpdatedBy: request.ChangedBy);

        var result = await _knowledgeService.UpsertKnowledgeAsync(
            OwnerId, namespaceId, signatureHash, knowledge, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<KnowledgeResponse>(result.Error);

        _cache.Remove($"dlq-signatures:{OwnerId}:{namespaceId}");

        return Ok(ToKnowledgeResponse(result.Value)!);
    }

    /// <summary>
    /// Gets prior versions of a failure signature's operational knowledge, most recent first.
    /// </summary>
    /// <param name="namespaceId">The namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's stable identity hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("~/" + ApiRoutes.Dlq.SignatureKnowledgeHistory)]
    [RequireNamespaceOwnership]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(IReadOnlyList<FailureKnowledgeHistoryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<FailureKnowledgeHistoryResponse>>> GetKnowledgeHistory(
        Guid namespaceId,
        string signatureHash,
        CancellationToken cancellationToken = default)
    {
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
            return ToActionResult<IReadOnlyList<FailureKnowledgeHistoryResponse>>(namespaceResult.Error);

        var result = await _knowledgeService.GetKnowledgeHistoryAsync(
            OwnerId, namespaceId, signatureHash, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<IReadOnlyList<FailureKnowledgeHistoryResponse>>(result.Error);

        var response = result.Value.Select(e => new FailureKnowledgeHistoryResponse(
            KnowledgeVersion: e.KnowledgeVersion,
            RootCause: e.RootCause,
            ResolutionNotes: e.ResolutionNotes,
            OperationalNotes: e.OperationalNotes,
            RunbookLink: e.RunbookLink,
            Owner: e.Owner,
            ReplayGuidance: e.ReplayGuidance,
            Tags: e.Tags,
            ReviewDueAt: e.ReviewDueAt,
            UpdatedBy: e.UpdatedBy,
            UpdatedAt: e.UpdatedAt)).ToList();

        return Ok(response);
    }

    /// <summary>
    /// Marks a failure signature's knowledge as needing review by the given date. Invalidates
    /// the cached signature list so the change is reflected immediately rather than for up to 60s.
    /// </summary>
    /// <param name="namespaceId">The namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's stable identity hash.</param>
    /// <param name="request">The review-due date.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("~/" + ApiRoutes.Dlq.SignatureKnowledgeReview)]
    [RequireNamespaceOwnership]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(KnowledgeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<KnowledgeResponse>> MarkKnowledgeForReview(
        Guid namespaceId,
        string signatureHash,
        [FromBody] MarkForReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        var namespaceResult = await GetOwnedNamespaceAsync(_namespaceRepository, namespaceId, cancellationToken);
        if (namespaceResult.IsFailure)
            return ToActionResult<KnowledgeResponse>(namespaceResult.Error);

        var result = await _knowledgeService.MarkForReviewAsync(
            OwnerId, namespaceId, signatureHash, request.ReviewDueAt, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<KnowledgeResponse>(result.Error);

        _cache.Remove($"dlq-signatures:{OwnerId}:{namespaceId}");

        return Ok(ToKnowledgeResponse(result.Value)!);
    }

    private static string GenerateCsv(IReadOnlyList<Core.Entities.DlqMessage> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,MessageId,SequenceNumber,CloudProvider,EntityName,EntityType,EnqueuedTimeUtc,DeadLetterTimeUtc,DetectedAtUtc,DeadLetterReason,DeliveryCount,FailureCategory,Status,BodyPreview");

        foreach (var m in messages)
        {
            sb.AppendLine(string.Join(",",
                m.Id,
                EscapeCsv(m.MessageId),
                m.SequenceNumber,
                m.CloudProvider,
                EscapeCsv(m.EntityName),
                m.EntityType,
                m.EnqueuedTimeUtc.ToString("o"),
                m.DeadLetterTimeUtc?.ToString("o") ?? "",
                m.DetectedAtUtc.ToString("o"),
                EscapeCsv(m.DeadLetterReason ?? ""),
                m.DeliveryCount,
                m.FailureCategory,
                m.Status,
                EscapeCsv(m.BodyPreview ?? "")));
        }

        return sb.ToString();
    }

    /// <summary>
    /// Manually triggers a DLQ scan for a namespace for instant updates.
    /// </summary>
    /// <param name="namespaceId">The namespace to scan.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of new messages found.</returns>
    [HttpPost("scan/{namespaceId:guid}")]
    [RequireScope(ApiKeyScopes.DlqWrite)]
    [ProducesResponseType(typeof(int), StatusCodes.Status200OK)]
    public async Task<ActionResult<int>> TriggerScan(
        Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        var monitorService = HttpContext.RequestServices.GetRequiredService<IDlqMonitorService>();
        var result = await monitorService.ScanNamespaceAsync(namespaceId, cancellationToken);

        if (result.IsSuccess)
            _cache.Remove($"dlq-signatures:{OwnerId}:{namespaceId}");

        return ToActionResult(result);
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
