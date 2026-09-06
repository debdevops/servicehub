using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// <see cref="DlqDbContext.DriftFindings"/>-backed implementation of
/// <see cref="IDriftResultCache"/> (roadmap next-chapter M1.1, ADR-0009) — replaces the
/// process-local, 24-hour TTL <c>InMemoryDriftResultCache</c>. This is the entity a hash-chained
/// <see cref="PlaybookEntry"/> proposal cites via <c>EvidenceRefJson</c>, which is exactly why it
/// needed to survive a restart.
/// </summary>
public sealed class SqliteDriftResultCache : IDriftResultCache
{
    private readonly DlqDbContext _dbContext;

    public SqliteDriftResultCache(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task StoreAsync(string ownerId, IEnumerable<DriftFinding> findings, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(findings);

        foreach (var finding in findings)
        {
            _dbContext.DriftFindings.Add(finding);
            // OwnerId is an EF-only shadow property — see DlqDbContext.ConfigureDriftFinding.
            _dbContext.Entry(finding).Property("OwnerId").CurrentValue = ownerId;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<DriftFinding?> TryGetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.DriftFindings
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }
}
