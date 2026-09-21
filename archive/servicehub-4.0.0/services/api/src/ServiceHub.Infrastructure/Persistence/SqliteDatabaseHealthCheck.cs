using System.Data.Common;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Health check for the SQLite database file backing <see cref="DlqDbContext"/>: file size (of
/// the main database file and its WAL/SHM siblings), WAL-checkpoint status, and the check's own
/// round-trip duration as a slow-query-equivalent signal. Delivered entirely through the
/// existing ASP.NET Core health check pipeline (roadmap §8 item 4 / F-track: basic DB
/// observability) — no new schema, no new persisted table.
/// </summary>
public sealed class SqliteDatabaseHealthCheck : IHealthCheck
{
    private const string DatabaseFileName = "servicehub-dlq.db";

    private readonly DlqDbContext _dbContext;
    private readonly IConfiguration _configuration;
    private readonly SqliteDatabaseHealthCheckOptions _options;
    private readonly ILogger<SqliteDatabaseHealthCheck> _logger;

    /// <summary>Initializes a new instance of the <see cref="SqliteDatabaseHealthCheck"/> class.</summary>
    public SqliteDatabaseHealthCheck(
        DlqDbContext dbContext,
        IConfiguration configuration,
        SqliteDatabaseHealthCheckOptions options,
        ILogger<SqliteDatabaseHealthCheck> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var dbPath = ResolveDatabasePath();
            // Surfaced so an operator or auditor can see, without reading config, whether this
            // instance is writing to a durable, operator-chosen path or the app-base fallback
            // (roadmap F1 — the ledger's durability should be visible, not just configured).
            data["DataDirectory"] = Path.GetDirectoryName(dbPath) ?? dbPath;

            if (!File.Exists(dbPath))
            {
                return HealthCheckResult.Unhealthy("SQLite database file not found.", data: data);
            }

            var dbSizeBytes = new FileInfo(dbPath).Length;
            var walSizeBytes = GetFileSizeOrZero(dbPath + "-wal");
            var shmSizeBytes = GetFileSizeOrZero(dbPath + "-shm");

            data["DatabaseSizeBytes"] = dbSizeBytes;
            data["WalSizeBytes"] = walSizeBytes;
            data["ShmSizeBytes"] = shmSizeBytes;
            data["TotalSizeBytes"] = dbSizeBytes + walSizeBytes + shmSizeBytes;

            string? journalMode;
            long checkpointBusy, walLogFrames, walCheckpointedFrames;

            // EF Core's own open-connection reference count, not a raw ADO.NET open/close —
            // safe to nest under a request that may already have the connection open, and never
            // leaves it open longer than this check needed it.
            await _dbContext.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var connection = _dbContext.Database.GetDbConnection();
                journalMode = await ExecuteScalarStringAsync(connection, "PRAGMA journal_mode;", cancellationToken)
                    .ConfigureAwait(false);
                (checkpointBusy, walLogFrames, walCheckpointedFrames) =
                    await ExecuteWalCheckpointAsync(connection, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await _dbContext.Database.CloseConnectionAsync().ConfigureAwait(false);
            }

            data["JournalMode"] = journalMode ?? "unknown";
            data["WalCheckpointBusy"] = checkpointBusy != 0;
            data["WalLogFrames"] = walLogFrames;
            data["WalCheckpointedFrames"] = walCheckpointedFrames;

            stopwatch.Stop();
            data["CheckDurationMs"] = stopwatch.Elapsed.TotalMilliseconds;

            var isSlow = stopwatch.Elapsed >= _options.SlowCheckThreshold;
            if (isSlow)
            {
                _logger.LogWarning(
                    "SQLite health check took {DurationMs}ms, exceeding the {ThresholdMs}ms slow-query-equivalent threshold.",
                    stopwatch.Elapsed.TotalMilliseconds,
                    _options.SlowCheckThreshold.TotalMilliseconds);
            }

            if (!string.Equals(journalMode, "wal", StringComparison.OrdinalIgnoreCase))
            {
                return HealthCheckResult.Degraded(
                    $"SQLite journal mode is '{journalMode}', expected 'wal'.", data: data);
            }

            if (walSizeBytes > _options.WalSizeWarningThresholdBytes)
            {
                return HealthCheckResult.Degraded(
                    $"WAL file size ({walSizeBytes} bytes) exceeds the warning threshold " +
                    $"({_options.WalSizeWarningThresholdBytes} bytes) — checkpointing may be falling behind.",
                    data: data);
            }

            if (isSlow)
            {
                return HealthCheckResult.Degraded(
                    $"SQLite health check took {stopwatch.Elapsed.TotalMilliseconds}ms, " +
                    $"exceeding the {_options.SlowCheckThreshold.TotalMilliseconds}ms threshold.",
                    data: data);
            }

            return HealthCheckResult.Healthy("SQLite database is healthy.", data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SQLite database health check");
            return HealthCheckResult.Unhealthy(
                "SQLite database health check failed with an exception.", ex, data);
        }
    }

    /// <summary>Same <c>DlqDatabase:DataDirectory</c> resolution <see cref="DependencyInjection.AddDlqDatabase"/>
    /// itself uses, duplicated here rather than shared — mirrors <see cref="Backup.BackupService"/>'s
    /// own duplicated resolution of the sibling namespace-store path for the same reason: this
    /// class must not take a hard dependency on the DI registration method's internals.</summary>
    private string ResolveDatabasePath()
    {
        var dataDir = _configuration["DlqDatabase:DataDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        return Path.Combine(dataDir, DatabaseFileName);
    }

    private static long GetFileSizeOrZero(string path) =>
        File.Exists(path) ? new FileInfo(path).Length : 0;

    private static async Task<string?> ExecuteScalarStringAsync(
        DbConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        var result = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        return result as string;
    }

    /// <summary>Runs a PASSIVE WAL checkpoint — never blocks writers or readers, unlike FULL/
    /// RESTART/TRUNCATE — purely to read back SQLite's own view of outstanding vs. checkpointed
    /// WAL frames as a real (not inferred) checkpoint-status signal.</summary>
    private static async Task<(long Busy, long LogFrames, long CheckpointedFrames)> ExecuteWalCheckpointAsync(
        DbConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA wal_checkpoint(PASSIVE);";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return (reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2));
        }

        return (0, -1, -1);
    }
}
