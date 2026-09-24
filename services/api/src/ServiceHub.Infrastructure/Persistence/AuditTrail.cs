using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>SQLite-backed <see cref="IAuditTrail"/>. Append-only from the application's side.</summary>
public sealed class AuditTrail : IAuditTrail
{
    /// <summary>The most entries one page may hold; a larger request is clamped, not refused.</summary>
    public const int MaxPageSize = 200;

    private readonly ServiceHubDbContext _dbContext;

    /// <summary>Creates the trail over the request-scoped database context.</summary>
    public AuditTrail(ServiceHubDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task RecordAsync(AuditLog entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _dbContext.AuditLogs.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<AuditPage> QueryAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var matching = _dbContext.AuditLogs.AsNoTracking().Where(a => a.OwnerId == query.OwnerId);

        if (query.NamespaceId is { } namespaceId)
        {
            matching = matching.Where(a => a.NamespaceId == namespaceId);
        }

        if (!string.IsNullOrWhiteSpace(query.Action))
        {
            matching = matching.Where(a => a.Action == query.Action);
        }

        // A restricted caller sees entries about its namespaces and entries about none in particular,
        // and never learns how many entries about anything else exist.
        if (query.AllowedNamespaceIds is { } allowed)
        {
            var ids = allowed.ToList();
            matching = matching.Where(a => a.NamespaceId == null || ids.Contains(a.NamespaceId.Value));
        }

        var total = await matching.CountAsync(cancellationToken).ConfigureAwait(false);

        var pageSize = Math.Clamp(query.PageSize, 1, MaxPageSize);
        var page = Math.Max(query.Page, 1);

        var items = await matching
            .OrderByDescending(a => a.Timestamp)
            .ThenByDescending(a => a.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new AuditPage(items, total);
    }
}
