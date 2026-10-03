namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Tunables for <see cref="SqliteDatabaseHealthCheck"/>. Bound from the
/// <c>DlqDatabase:HealthCheck</c> configuration section (roadmap §8 item 4 / F-track: basic DB
/// observability).
/// </summary>
public sealed class SqliteDatabaseHealthCheckOptions
{
    /// <summary>Defaults applied when DI does not supply an instance — e.g. tests constructing
    /// <see cref="SqliteDatabaseHealthCheck"/> directly.</summary>
    public static readonly SqliteDatabaseHealthCheckOptions Default = new();

    /// <summary>WAL file size, in bytes, above which the check reports Degraded. A growing WAL
    /// file (rather than one that oscillates back down after each checkpoint) is the leading
    /// indicator that checkpointing is falling behind the eight background workers writing to
    /// this database.</summary>
    public long WalSizeWarningThresholdBytes { get; init; } = 64 * 1024 * 1024;

    /// <summary>Health check duration above which a warning is logged and the result is
    /// reported Degraded. SQLite has no native slow-query log; this check's own round-trip time
    /// against the live database (file stat + PRAGMA journal_mode + PRAGMA wal_checkpoint) is
    /// the closest deterministic equivalent, surfaced through the health check infrastructure
    /// that already exists rather than a new per-command interceptor.</summary>
    public TimeSpan SlowCheckThreshold { get; init; } = TimeSpan.FromMilliseconds(500);
}
