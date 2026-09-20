using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// <see cref="DlqDbContext.Anomalies"/>-backed implementation of <see cref="IAnomalyResultCache"/>
/// (roadmap next-chapter M1.1, ADR-0009) — replaces the process-local, 24-hour TTL
/// <c>InMemoryAnomalyResultCache</c>.
/// </summary>
public sealed class SqliteAnomalyResultCache : IAnomalyResultCache
{
    private readonly DlqDbContext _dbContext;

    public SqliteAnomalyResultCache(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task StoreAsync(string ownerId, IEnumerable<Anomaly> anomalies, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(anomalies);

        foreach (var anomaly in anomalies)
        {
            _dbContext.Anomalies.Add(anomaly);
            // OwnerId is an EF-only shadow property — Anomaly's own CLR type stays owner-agnostic
            // (see DlqDbContext.ConfigureAnomaly's remarks).
            _dbContext.Entry(anomaly).Property("OwnerId").CurrentValue = ownerId;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<Anomaly?> TryGetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.Anomalies
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }
}
