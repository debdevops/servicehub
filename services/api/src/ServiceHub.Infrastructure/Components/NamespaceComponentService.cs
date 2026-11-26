using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Components;

/// <inheritdoc cref="INamespaceComponentService"/>
public sealed class NamespaceComponentService : INamespaceComponentService
{
    private readonly ServiceHubDbContext _db;

    /// <summary>Creates the service.</summary>
    public NamespaceComponentService(ServiceHubDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<NamespaceComponent>> ListAsync(string ownerId, Guid namespaceId, CancellationToken cancellationToken = default) =>
        await _db.NamespaceComponents.AsNoTracking().Where(c => c.OwnerId == ownerId && c.NamespaceId == namespaceId).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<NamespaceComponent>> ListAllAsync(string ownerId, CancellationToken cancellationToken = default) =>
        await _db.NamespaceComponents.AsNoTracking().Where(c => c.OwnerId == ownerId).ToListAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Result<NamespaceComponent>> SetAsync(
        string ownerId, Guid namespaceId, string componentKey, bool enabled, string changedBy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentKey))
        {
            return Result<NamespaceComponent>.Failure(Error.Validation("NamespaceComponent.KeyRequired", "A component is required."));
        }

        var key = componentKey.Trim();
        var row = await _db.NamespaceComponents.FirstOrDefaultAsync(
            c => c.OwnerId == ownerId && c.NamespaceId == namespaceId && c.ComponentKey == key, cancellationToken);
        if (row is null)
        {
            row = new NamespaceComponent { OwnerId = ownerId, NamespaceId = namespaceId, ComponentKey = key, Enabled = enabled, ChangedAt = DateTimeOffset.UtcNow, ChangedBy = changedBy };
            _db.NamespaceComponents.Add(row);
        }
        else if (row.Enabled != enabled)
        {
            row.Enabled = enabled;
            row.ChangedAt = DateTimeOffset.UtcNow;
            row.ChangedBy = changedBy;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return Result<NamespaceComponent>.Success(row);
    }
}
