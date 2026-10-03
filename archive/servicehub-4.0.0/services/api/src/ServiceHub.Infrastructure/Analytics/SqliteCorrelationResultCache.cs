using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// <see cref="DlqDbContext.CorrelationFindings"/>-backed implementation of
/// <see cref="ICorrelationResultCache"/> (roadmap next-chapter M1.1, ADR-0009) — replaces the
/// process-local, 24-hour TTL <c>InMemoryCorrelationResultCache</c>.
/// </summary>
public sealed class SqliteCorrelationResultCache : ICorrelationResultCache
{
    private readonly DlqDbContext _dbContext;

    public SqliteCorrelationResultCache(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task StoreAsync(IEnumerable<CorrelationFinding> findings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(findings);

        _dbContext.CorrelationFindings.AddRange(findings);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<CorrelationFinding?> TryGetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.CorrelationFindings
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }
}
