using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for DLQ Intelligence operations.
/// Provides historical tracking, timeline views, and export capabilities.
/// </summary>
[Route(ApiRoutes.Dlq.Base)]
[Tags("DLQ Intelligence")]
public sealed class DlqHistoryController : ApiControllerBase
{
    private readonly IDlqHistoryService _historyService;
    private readonly ILogger<DlqHistoryController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DlqHistoryController"/> class.
    /// </summary>
    public DlqHistoryController(
        IDlqHistoryService historyService,
        ILogger<DlqHistoryController> logger)
    {
        _historyService = historyService ?? throw new ArgumentNullException(nameof(historyService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            namespaceId, entityName, from, to, status, category,
            page, pageSize, cancellationToken);

        if (result.IsFailure)
            return ToActionResult<PaginatedResponse<DlqHistoryResponse>>(result.Error);

        var data = result.Value;
        var items = data.Items.Select(m => new DlqHistoryResponse(
            Id: m.Id,
            MessageId: m.MessageId,
            SequenceNumber: m.SequenceNumber,
            BodyHash: m.BodyHash,
            NamespaceId: m.NamespaceId,
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
            UserNotes: m.UserNotes,
            CorrelationId: m.CorrelationId,
            TopicName: m.TopicName
        )).ToList();

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
        var result = await _historyService.GetByIdAsync(id, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<DlqMessageDetailResponse>(result.Error);

        var m = result.Value;
        var response = new DlqMessageDetailResponse(
            Id: m.Id,
            MessageId: m.MessageId,
            SequenceNumber: m.SequenceNumber,
            BodyHash: m.BodyHash,
            NamespaceId: m.NamespaceId,
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
        var result = await _historyService.GetTimelineAsync(id, cancellationToken);
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
            EntityName: string.Empty, // Will be populated from the message
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
        var result = await _historyService.UpdateNotesAsync(id, request.Notes, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<DlqHistoryResponse>(result.Error);

        var m = result.Value;
        var response = new DlqHistoryResponse(
            Id: m.Id,
            MessageId: m.MessageId,
            SequenceNumber: m.SequenceNumber,
            BodyHash: m.BodyHash,
            NamespaceId: m.NamespaceId,
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
            UserNotes: m.UserNotes,
            CorrelationId: m.CorrelationId,
            TopicName: m.TopicName);

        return Ok(response);
    }

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
            namespaceId, entityName, from, to, status, cancellationToken);

        if (result.IsFailure)
            return ToActionResult(ServiceHub.Shared.Results.Result.Failure(result.Error));

        var messages = result.Value;

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = GenerateCsv(messages);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", "dlq-export.csv");
        }

        var jsonItems = messages.Select(m => new DlqHistoryResponse(
            Id: m.Id,
            MessageId: m.MessageId,
            SequenceNumber: m.SequenceNumber,
            BodyHash: m.BodyHash,
            NamespaceId: m.NamespaceId,
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
            UserNotes: m.UserNotes,
            CorrelationId: m.CorrelationId,
            TopicName: m.TopicName
        )).ToList();

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
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>DLQ summary statistics.</returns>
    [HttpGet("summary")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(DlqSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<DlqSummaryResponse>> GetSummary(
        [FromQuery] Guid? namespaceId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _historyService.GetSummaryAsync(namespaceId, cancellationToken);
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

    private static string GenerateCsv(IReadOnlyList<Core.Entities.DlqMessage> messages)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Id,MessageId,SequenceNumber,EntityName,EntityType,EnqueuedTimeUtc,DeadLetterTimeUtc,DetectedAtUtc,DeadLetterReason,DeliveryCount,FailureCategory,Status,BodyPreview");

        foreach (var m in messages)
        {
            sb.AppendLine(string.Join(",",
                m.Id,
                EscapeCsv(m.MessageId),
                m.SequenceNumber,
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

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
