using System.ComponentModel.DataAnnotations;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.DTOs.Requests;

/// <summary>
/// Selects the set of DLQ messages a bulk operation targets. Mirrors
/// <see cref="Interfaces.IDlqHistoryService.GetHistoryAsync"/>'s filter parameters exactly, so
/// "bulk replay everything matching my current DLQ History filter" requires no new filter UI —
/// the frontend passes the same filter object it already built for the history query.
/// </summary>
/// <param name="NamespaceId">Required. Bulk operations are scoped to a single namespace.</param>
/// <param name="EntityName">Optional entity-name substring filter.</param>
/// <param name="From">Optional inclusive lower bound on detection time.</param>
/// <param name="To">Optional inclusive upper bound on detection time.</param>
/// <param name="Status">Optional status filter. Replay/purge only make sense against a specific status in practice (typically <c>Active</c>), but the filter itself is unrestricted here — validity is enforced by the service.</param>
/// <param name="Category">Optional failure-category filter.</param>
public sealed record BulkOperationFilterRequest(
    [Required] Guid NamespaceId,
    string? EntityName,
    DateTimeOffset? From,
    DateTimeOffset? To,
    DlqMessageStatus? Status,
    FailureCategory? Category);

/// <summary>Request body for <c>POST /api/v1/bulk-operations/preview</c>.</summary>
/// <param name="OperationType">Replay or Purge — determines which capability/safety checks the preview surfaces.</param>
/// <param name="Filter">The message-selection filter.</param>
public sealed record BulkOperationPreviewRequest(
    [Required] BulkOperationType OperationType,
    [Required] BulkOperationFilterRequest Filter);

/// <summary>Request body for <c>POST /api/v1/bulk-operations</c>.</summary>
/// <param name="OperationType">Replay or Purge.</param>
/// <param name="Filter">The message-selection filter, identical to what was used for the preview.</param>
public sealed record BulkOperationCreateRequest(
    [Required] BulkOperationType OperationType,
    [Required] BulkOperationFilterRequest Filter);
