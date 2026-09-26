using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>SQLite-backed <see cref="IAuditTrail"/>. Append-only from the application's side.</summary>
public sealed class AuditTrail : IAuditTrail
{
    /// <summary>The most entries one page may hold; a larger request is clamped, not refused.</summary>
    public const int MaxPageSize = 200;

    private readonly ServiceHubDbContext _dbContext;
    private readonly IPlatformEventBus? _events;

    /// <summary>Creates the trail over the request-scoped database context.</summary>
    public AuditTrail(ServiceHubDbContext dbContext, IPlatformEventBus? events = null)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _events = events;
    }

    /// <inheritdoc />
    public async Task RecordAsync(AuditLog entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        _dbContext.AuditLogs.Add(entry);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // After it is durable, tell anyone watching to look again. A hint only: the row above is the truth, and a
        // publish that goes nowhere loses nothing.
        if (_events is not null && EventFor(entry) is { } kind)
        {
            await _events.PublishAsync(new PlatformEvent
            {
                Source = "audit", Category = kind.Category, EventType = kind.Type,
                CloudProvider = entry.CloudProvider, NamespaceId = entry.NamespaceId, NamespaceName = entry.NamespaceName,
                CorrelationId = entry.CorrelationId, Actor = entry.OwnerId,
            }, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Which event, if any, an audit row announces. Only things that succeeded change what a screen shows.</summary>
    private static (string Category, string Type)? EventFor(AuditLog entry)
    {
        if (!string.Equals(entry.Outcome, AuditActions.Success, StringComparison.Ordinal))
        {
            return null;
        }

        return entry.Action switch
        {
            AuditActions.NamespaceConnect => (EventCategories.Namespace, EventTypes.NamespaceCreated),
            AuditActions.NamespaceRemove => (EventCategories.Namespace, EventTypes.NamespaceDeleted),
            AuditActions.ReplayMessage => (EventCategories.Replay, EventTypes.ReplayCompleted),
            _ => null,
        };
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

        if (query.ScopeNamespaceIds is { } scope)
        {
            var scoped = scope.ToList();
            matching = matching.Where(a => a.NamespaceId != null && scoped.Contains(a.NamespaceId.Value));
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
