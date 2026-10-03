namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Response DTO for a single audit log entry. All fields map 1-to-1 from the
/// AuditLog entity so the API surface is self-describing.
/// </summary>
public sealed record AuditLogResponse(
    Guid Id,
    DateTimeOffset Timestamp,
    string UserIdentity,
    string Action,
    string Outcome,
    Guid? NamespaceId,
    string? NamespaceName,
    string? EntityName,
    string? CloudProvider,
    string? Environment,
    string? ResourceName,
    long? SequenceNumber,
    string? DetailsJson,
    string? ErrorDetails,
    string? ClientIp,
    string? UserAgent,
    string? CorrelationId,
    string? HttpMethod,
    string? HttpPath);

/// <summary>
/// Paginated response for audit log list queries.
/// </summary>
public sealed record AuditPageResponse(
    IReadOnlyList<AuditLogResponse> Items,
    int TotalCount,
    int Page,
    int PageSize,
    bool HasNextPage,
    bool HasPreviousPage);

/// <summary>
/// High-level statistics response for the audit trail summary panel.
/// </summary>
public sealed record AuditSummaryResponse(
    int TotalEvents,
    int SuccessCount,
    int FailureCount,
    int PartialCount,
    int ActiveUsers,
    double SuccessRate);

/// <summary>Response for an on-demand audit log retention purge.</summary>
public sealed record PurgeAuditLogsResponse(int DeletedCount, DateTimeOffset CutoffUtc);
