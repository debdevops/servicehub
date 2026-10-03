namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Tunables for the SQLITE_BUSY/SQLITE_LOCKED retry wrapper around
/// <see cref="ServiceHubDbContext.SaveChanges(bool)"/> and
/// <see cref="ServiceHubDbContext.SaveChangesAsync(bool, System.Threading.CancellationToken)"/>.
/// Bound from the <c>ServiceHub:MaxBusyRetryAttempts</c> configuration key.
/// </summary>
public sealed class SqliteBusyRetryOptions
{
    /// <summary>Defaults applied when DI does not supply an instance — e.g. test fixtures
    /// constructing <see cref="ServiceHubDbContext"/> directly with only a
    /// <c>DbContextOptions&lt;ServiceHubDbContext&gt;</c> argument.</summary>
    public static readonly SqliteBusyRetryOptions Default = new();

    /// <summary>Number of retries attempted after the first failed SaveChanges call when the
    /// failure is SQLITE_BUSY or SQLITE_LOCKED. busy_timeout already absorbs short contention
    /// inside the SQLite driver itself; this is the outer safety net for when contention
    /// outlasts that.</summary>
    public int MaxRetryAttempts { get; init; } = 3;
}
