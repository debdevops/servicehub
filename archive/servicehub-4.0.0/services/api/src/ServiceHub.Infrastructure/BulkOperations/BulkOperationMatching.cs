using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.BulkOperations;

/// <summary>
/// Builds the DLQ-message match query shared by <see cref="BulkOperationService"/> (preview and
/// job creation) and <see cref="BulkOperationExecutor"/> (actual execution), so the set of
/// messages a job executes against is always derived the same way it was counted and sampled —
/// there is exactly one place that translates a bulk-operation filter into a query.
/// </summary>
internal static class BulkOperationMatching
{
    /// <param name="dbContext">The DLQ database context to query.</param>
    /// <param name="ownerId">Tenant-isolation filter — only this owner's messages are matched.</param>
    /// <param name="namespaceId">The namespace the bulk operation is scoped to.</param>
    /// <param name="entityName">Optional entity-name substring filter.</param>
    /// <param name="status">Optional status filter.</param>
    /// <param name="category">Optional failure-category filter.</param>
    /// <param name="from">Optional inclusive lower bound on detection time.</param>
    /// <param name="to">Optional inclusive upper bound on detection time.</param>
    /// <param name="trackChanges">
    /// <see langword="false"/> (default) for read-only use — preview, counting, sampling.
    /// <see langword="true"/> for the executor, which mutates <see cref="DlqMessage.Status"/>
    /// on each processed message and needs EF change tracking for those updates to persist.
    /// </param>
    public static IQueryable<DlqMessage> BuildQuery(
        DlqDbContext dbContext,
        string ownerId,
        Guid namespaceId,
        string? entityName,
        DlqMessageStatus? status,
        FailureCategory? category,
        DateTimeOffset? from,
        DateTimeOffset? to,
        bool trackChanges = false)
    {
        var baseQuery = dbContext.DlqMessages.Where(m => m.OwnerId == ownerId && m.NamespaceId == namespaceId);
        var query = trackChanges ? baseQuery : baseQuery.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(entityName))
            query = query.Where(m => m.EntityName.Contains(entityName));

        if (status.HasValue)
            query = query.Where(m => m.Status == status.Value);

        if (category.HasValue)
            query = query.Where(m => m.FailureCategory == category.Value);

        if (from.HasValue)
            query = query.Where(m => m.DetectedAtUtc >= from.Value);

        if (to.HasValue)
            query = query.Where(m => m.DetectedAtUtc <= to.Value);

        return query;
    }
}
