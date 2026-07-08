using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Controller for the Persistent Audit Trail.
/// Exposes paginated queries, summary statistics, and CSV/JSON exports.
/// All endpoints are tenant-scoped — users can only see their own audit logs.
/// </summary>
[Route(ApiRoutes.Audit.Base)]
[Tags("Audit Trail")]
public sealed class AuditController : ApiControllerBase
{
    private readonly IAuditService _auditService;
    private readonly ILogger<AuditController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AuditController"/> class.
    /// </summary>
    public AuditController(IAuditService auditService, ILogger<AuditController> logger)
    {
        _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ─── GET /api/v1/audit ────────────────────────────────────────────────────

    /// <summary>
    /// Gets a paginated list of audit log entries for the authenticated owner.
    /// </summary>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="search">Full-text search across user identity, action, resource name, and details.</param>
    /// <param name="actionType">Action category prefix filter (e.g. "Messages", "Rule", "Namespace").</param>
    /// <param name="outcome">Outcome filter: "Success", "Failure", or "Partial".</param>
    /// <param name="from">Start of time range (inclusive).</param>
    /// <param name="to">End of time range (inclusive).</param>
    /// <param name="page">Page number (default 1).</param>
    /// <param name="pageSize">Items per page (default 50, max 200).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Paginated list of audit log entries.</returns>
    [HttpGet]
    [RequireScope(ApiKeyScopes.AuditRead)]
    [ProducesResponseType(typeof(AuditPageResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuditPageResponse>> GetLogs(
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] string? search = null,
        [FromQuery] string? actionType = null,
        [FromQuery] string? outcome = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 200);
        page = Math.Max(page, 1);

        var result = await _auditService.GetLogsAsync(
            OwnerId, namespaceId, search, actionType, outcome, from, to,
            page, pageSize, cancellationToken);

        if (result.IsFailure)
            return ToActionResult<AuditPageResponse>(result.Error);

        var data = result.Value;
        var items = data.Items.Select(MapToResponse).ToList();

        return Ok(new AuditPageResponse(
            Items: items,
            TotalCount: data.TotalCount,
            Page: data.Page,
            PageSize: data.PageSize,
            HasNextPage: data.HasNextPage,
            HasPreviousPage: data.HasPreviousPage));
    }

    // ─── GET /api/v1/audit/summary ────────────────────────────────────────────

    /// <summary>
    /// Gets high-level audit trail statistics for the authenticated owner.
    /// </summary>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Audit trail summary statistics.</returns>
    [HttpGet("summary")]
    [RequireScope(ApiKeyScopes.AuditRead)]
    [ProducesResponseType(typeof(AuditSummaryResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AuditSummaryResponse>> GetSummary(
        [FromQuery] Guid? namespaceId = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _auditService.GetSummaryAsync(OwnerId, namespaceId, cancellationToken);
        if (result.IsFailure)
            return ToActionResult<AuditSummaryResponse>(result.Error);

        var s = result.Value;
        return Ok(new AuditSummaryResponse(
            TotalEvents: s.TotalEvents,
            SuccessCount: s.SuccessCount,
            FailureCount: s.FailureCount,
            PartialCount: s.PartialCount,
            ActiveUsers: s.ActiveUsers,
            SuccessRate: s.SuccessRate));
    }

    // ─── GET /api/v1/audit/export ─────────────────────────────────────────────

    /// <summary>
    /// Exports audit log entries as a downloadable CSV or JSON file.
    /// Applies the same filters as the list endpoint.
    /// </summary>
    /// <param name="format">Export format: "csv" (default) or "json".</param>
    /// <param name="namespaceId">Optional namespace filter.</param>
    /// <param name="actionType">Action category prefix filter.</param>
    /// <param name="outcome">Outcome filter.</param>
    /// <param name="from">Start of time range.</param>
    /// <param name="to">End of time range.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Downloadable file stream.</returns>
    [HttpGet("export")]
    [RequireScope(ApiKeyScopes.AuditRead)]
    [ProducesResponseType(typeof(FileResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> Export(
        [FromQuery] string format = "csv",
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] string? actionType = null,
        [FromQuery] string? outcome = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _auditService.ExportAsync(
            OwnerId, namespaceId, actionType, outcome, from, to, cancellationToken);

        if (result.IsFailure)
            return ToActionResult(ServiceHub.Shared.Results.Result.Failure(result.Error));

        var entries = result.Value;
        var dateStamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

        if (string.Equals(format, "csv", StringComparison.OrdinalIgnoreCase))
        {
            var csv = GenerateCsv(entries);
            return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"audit-export-{dateStamp}.csv");
        }

        var responses = entries.Select(MapToResponse).ToList();
        var json = JsonSerializer.Serialize(responses, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"audit-export-{dateStamp}.json");
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    private static AuditLogResponse MapToResponse(Core.Entities.AuditLog log) =>
        new(
            Id: log.Id,
            Timestamp: log.Timestamp,
            UserIdentity: log.UserIdentity,
            Action: log.Action,
            Outcome: log.Outcome,
            NamespaceId: log.NamespaceId,
            NamespaceName: log.NamespaceName,
            EntityName: log.EntityName,
            CloudProvider: log.CloudProvider,
            Environment: log.Environment,
            ResourceName: log.ResourceName,
            SequenceNumber: log.SequenceNumber,
            DetailsJson: log.DetailsJson,
            ErrorDetails: log.ErrorDetails,
            ClientIp: log.ClientIp,
            UserAgent: log.UserAgent,
            CorrelationId: log.CorrelationId,
            HttpMethod: log.HttpMethod,
            HttpPath: log.HttpPath);

    private static string GenerateCsv(IReadOnlyList<Core.Entities.AuditLog> entries)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Timestamp,UserIdentity,Action,Outcome,NamespaceName,EntityName,CloudProvider,Environment,ResourceName,SequenceNumber,CorrelationId,ClientIp,HttpMethod,HttpPath,Details");

        foreach (var e in entries)
        {
            sb.AppendLine(string.Join(",",
                e.Timestamp.ToString("o"),
                EscapeCsv(e.UserIdentity),
                EscapeCsv(e.Action),
                EscapeCsv(e.Outcome),
                EscapeCsv(e.NamespaceName ?? string.Empty),
                EscapeCsv(e.EntityName ?? string.Empty),
                EscapeCsv(e.CloudProvider ?? string.Empty),
                EscapeCsv(e.Environment ?? string.Empty),
                EscapeCsv(e.ResourceName ?? string.Empty),
                e.SequenceNumber?.ToString() ?? string.Empty,
                EscapeCsv(e.CorrelationId ?? string.Empty),
                EscapeCsv(e.ClientIp ?? string.Empty),
                EscapeCsv(e.HttpMethod ?? string.Empty),
                EscapeCsv(e.HttpPath ?? string.Empty),
                EscapeCsv(e.DetailsJson ?? string.Empty)));
        }

        return sb.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // Neutralise spreadsheet formula injection: a leading =, +, -, @, tab or CR
        // would otherwise be evaluated as a formula when the CSV is opened in Excel.
        if (value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            value = "'" + value;

        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return $"\"{value.Replace("\"", "\"\"")}\"";
        return value;
    }
}
