using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Readiness check for the SQLite file: it exists, it answers a query, it is in WAL mode, and its
/// size is visible. Written for 4.1.0 rather than ported — 4.0.0's version reported WAL-checkpoint
/// and slow-query signals no screen reads yet (R12).
/// </summary>
public sealed class SqliteDatabaseHealthCheck(ServiceHubDbContext dbContext, IConfiguration configuration) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var dbPath = ServiceHubDataDirectory.ResolveDatabasePath(configuration);
        var data = new Dictionary<string, object>
        {
            ["DataDirectory"] = Path.GetDirectoryName(dbPath) ?? dbPath,
        };

        try
        {
            if (!File.Exists(dbPath))
            {
                return HealthCheckResult.Unhealthy("SQLite database file not found.", data: data);
            }

            data["DatabaseSizeBytes"] = new FileInfo(dbPath).Length;

            string? journalMode;
            await dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await using var command = dbContext.Database.GetDbConnection().CreateCommand();
                command.CommandText = "PRAGMA journal_mode;";
                journalMode = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) as string;
            }
            finally
            {
                await dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
            }

            data["JournalMode"] = journalMode ?? "unknown";

            return string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase)
                ? HealthCheckResult.Healthy("SQLite database is healthy.", data: data)
                : HealthCheckResult.Degraded($"SQLite journal mode is '{journalMode}', expected 'wal'.", data: data);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("SQLite database health check failed.", ex, data);
        }
    }
}
