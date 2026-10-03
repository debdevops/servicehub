using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// <see cref="DlqDbContext.Narrations"/>-backed implementation of
/// <see cref="INarrationResultCache"/> (roadmap next-chapter M1.1, ADR-0009) — replaces the
/// process-local, 24-hour TTL <c>InMemoryNarrationResultCache</c>.
/// </summary>
public sealed class SqliteNarrationResultCache : INarrationResultCache
{
    private readonly DlqDbContext _dbContext;

    public SqliteNarrationResultCache(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task StoreAsync(IEnumerable<Narration> narrations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(narrations);

        _dbContext.Narrations.AddRange(narrations);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Narration?> TryGetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Narrations
            .AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }
}
