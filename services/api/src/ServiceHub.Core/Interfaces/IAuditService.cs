using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Service for recording and querying the persistent audit trail.
/// All write operations are fire-and-forget (enqueued to a background channel);
/// all read operations are strictly tenant-scoped via ownerId.
/// </summary>
public interface IAuditService
{
    // ─── Write ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Enqueues an audit log entry for background persistence.
    /// This method must never block the calling request thread.
    /// </summary>
    void Enqueue(AuditLog entry);

    // ─── Read ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Gets a paginated, filtered list of audit log entries for the given owner.
    /// </summary>
    Task<Result<AuditPageResult>> GetLogsAsync(
        string ownerId,
        Guid? namespaceId = null,
        string? search = null,
        string? actionType = null,
        string? outcome = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        int page = 1,
        int pageSize = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a summary of audit trail activity for the given owner and optional namespace.
    /// </summary>
    Task<Result<AuditSummary>> GetSummaryAsync(
        string ownerId,
        Guid? namespaceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports all audit log entries matching the given filters as a list.
    /// </summary>
    Task<Result<IReadOnlyList<AuditLog>>> ExportAsync(
        string ownerId,
        Guid? namespaceId = null,
        string? actionType = null,
        string? outcome = null,
        DateTimeOffset? from = null,
        DateTimeOffset? to = null,
        CancellationToken cancellationToken = default);

    // ─── Retention ───────────────────────────────────────────────────────────

    /// <summary>
    /// Permanently deletes every audit log entry timestamped strictly before
    /// <paramref name="olderThan"/>, across all owners — retention is an instance-wide policy,
    /// not per-tenant. Used by the automatic retention sweep (<c>Audit:Retention:Enabled</c>)
    /// and available for on-demand purge.
    /// </summary>
    /// <param name="olderThan">The cutoff — entries timestamped before this are deleted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The number of entries deleted.</returns>
    Task<Result<int>> PurgeExpiredAsync(DateTimeOffset olderThan, CancellationToken cancellationToken = default);
}

/// <summary>Paginated result for audit log queries.</summary>
public sealed class AuditPageResult
{
    public required IReadOnlyList<AuditLog> Items { get; init; }
    public required int TotalCount { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public bool HasNextPage => Page * PageSize < TotalCount;
    public bool HasPreviousPage => Page > 1;
}

/// <summary>High-level summary statistics for the audit trail.</summary>
public sealed class AuditSummary
{
    public required int TotalEvents { get; init; }
    public required int SuccessCount { get; init; }
    public required int FailureCount { get; init; }
    public required int PartialCount { get; init; }
    public required int ActiveUsers { get; init; }
    public double SuccessRate => TotalEvents == 0 ? 0 : Math.Round((double)SuccessCount / TotalEvents * 100, 1);
}
