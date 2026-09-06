using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Analytics;

/// <summary>
/// <see cref="DlqDbContext.BacklogForecasts"/>-backed implementation of
/// <see cref="IBacklogForecastResultCache"/> (roadmap next-chapter M1.1, ADR-0009) — replaces the
/// process-local, 24-hour TTL <c>InMemoryBacklogForecastResultCache</c>.
/// </summary>
public sealed class SqliteBacklogForecastResultCache : IBacklogForecastResultCache
{
    private readonly DlqDbContext _dbContext;

    public SqliteBacklogForecastResultCache(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task StoreAsync(string ownerId, IEnumerable<BacklogForecast> forecasts, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(ownerId);
        ArgumentNullException.ThrowIfNull(forecasts);

        foreach (var forecast in forecasts)
        {
            _dbContext.BacklogForecasts.Add(forecast);
            // OwnerId is an EF-only shadow property — see DlqDbContext.ConfigureBacklogForecast.
            _dbContext.Entry(forecast).Property("OwnerId").CurrentValue = ownerId;
        }

        await _dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<BacklogForecast?> TryGetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _dbContext.BacklogForecasts
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == id, cancellationToken)
            .ConfigureAwait(false);
    }
}
