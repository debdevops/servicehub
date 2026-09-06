using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// <see cref="DlqDbContext.ExternalSignalCorrelations"/>-backed implementation of
/// <see cref="IExternalSignalCorrelationCache"/> (roadmap next-chapter M1.1, ADR-0009) — replaces
/// the process-local, 24-hour TTL <c>InMemoryExternalSignalCorrelationCache</c>.
/// </summary>
public sealed class SqliteExternalSignalCorrelationCache : IExternalSignalCorrelationCache
{
    private readonly DlqDbContext _dbContext;

    public SqliteExternalSignalCorrelationCache(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task StoreAsync(IEnumerable<ExternalSignalCorrelation> correlations, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(correlations);

        _dbContext.ExternalSignalCorrelations.AddRange(correlations);
        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ExternalSignalCorrelation?> TryGetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.ExternalSignalCorrelations
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }
}
