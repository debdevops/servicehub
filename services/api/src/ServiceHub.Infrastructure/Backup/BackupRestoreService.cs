using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Models.Backup;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.Infrastructure.Backup;

/// <summary>
/// Checks a backup bundle and stages it for the next start (unit 6.13). See <see cref="IBackupRestore"/>.
/// </summary>
/// <remarks>
/// Why staged, not swapped: a live SQLite file has open connections, a write-ahead log and agents writing to it. Replacing it
/// underneath them is how a restore corrupts the thing it was meant to bring back. <see cref="ApplyPendingRestore"/> runs at
/// start, after the instance lock and before the first connection, which is the only moment the file is nobody's.
/// </remarks>
public sealed class BackupRestoreService : IBackupRestore
{
    /// <summary>The staged database, beside the live one.</summary>
    public const string PendingFileName = ServiceHubDataDirectory.DatabaseFileName + ".restore-pending";

    private const string PendingMarkerName = PendingFileName + ".json";
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly ServiceHubDbContext _db;
    private readonly IConnectionStringProtector _protector;
    private readonly IConfiguration _configuration;
    private readonly BackupOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BackupRestoreService> _logger;

    /// <summary>Creates the service.</summary>
    public BackupRestoreService(
        ServiceHubDbContext db, IConnectionStringProtector protector, IConfiguration configuration, IOptions<BackupOptions> options,
        ILogger<BackupRestoreService> logger, TimeProvider? time = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _time = time ?? TimeProvider.System;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private string DataDir => ServiceHubDataDirectory.Resolve(_configuration);

    private string BackupRoot => string.IsNullOrWhiteSpace(_options.BackupDirectory) ? Path.Combine(DataDir, "backups") : _options.BackupDirectory;

    /// <summary>The bundle directory for an id, or null when the id is not a plain directory name under the backup root.</summary>
    public string? BundlePath(string backupId)
    {
        if (string.IsNullOrWhiteSpace(backupId) || backupId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || backupId.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        var dir = Path.Combine(BackupRoot, backupId);
        return Directory.Exists(dir) ? dir : null;
    }

    /// <inheritdoc />
    public async Task<RestoreCheck> CheckAsync(string backupId, CancellationToken cancellationToken = default)
    {
        var checks = new List<RestoreCheckItem>();
        RestoreCheck Done() => new(backupId, checks.Count > 0 && checks.All(c => c.Passed), checks);

        var dir = BundlePath(backupId);
        var manifest = dir is null ? null : ReadManifest(dir);
        if (dir is null || manifest is null)
        {
            checks.Add(new("The backup exists", false, $"There is no backup called '{backupId}' with a manifest."));
            return Done();
        }

        var snapshot = Path.Combine(dir, manifest.Sqlite.FileName);
        var intact = File.Exists(snapshot) && string.Equals(Sha256(snapshot), manifest.Sqlite.Sha256, StringComparison.OrdinalIgnoreCase);
        checks.Add(new("The file is the one that was backed up", intact,
            intact ? "Its checksum matches the manifest." : "Its checksum does not match the manifest — it was changed or damaged after the backup."));
        if (!intact) return Done();

        var integrity = await ScalarAsync(snapshot, "PRAGMA integrity_check;", cancellationToken).ConfigureAwait(false);
        var sound = string.Equals(integrity, "ok", StringComparison.OrdinalIgnoreCase);
        checks.Add(new("The database inside is sound", sound, sound ? "SQLite's integrity check passed." : $"SQLite's integrity check found: {integrity}"));
        if (!sound) return Done();

        var sameKey = string.Equals(manifest.EncryptionKeyFingerprint, _protector.GetKeyFingerprint(), StringComparison.Ordinal);
        checks.Add(new("It was made with this server's encryption key", sameKey, sameKey
            ? "The key fingerprints match, so its saved connections will open."
            : "It was made with a different encryption key, so its saved connections could not be opened here. Restore it on the server that holds that key, or bring that key here first."));

        var known = _db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);
        var applied = await MigrationsAsync(snapshot, cancellationToken).ConfigureAwait(false);
        var unknown = applied?.Where(m => !known.Contains(m)).ToList();
        var schemaOk = applied is { Count: > 0 } && unknown is { Count: 0 };
        checks.Add(new("Its schema is one this version knows", schemaOk,
            applied is null or { Count: 0 } ? "It holds no ServiceHub 4.1.0 schema history — it is not a ServiceHub 4.1.0 database."
            : schemaOk ? $"{applied.Count} of this build's {known.Count} schema steps; any missing ones are applied at start."
            : $"It was made by a newer ServiceHub ({string.Join(", ", unknown!)}). Restore it with that version or later."));
        if (!schemaOk) return Done();

        checks.Add(await VerifyChainsAsync(snapshot, cancellationToken).ConfigureAwait(false));
        return Done();
    }

    /// <inheritdoc />
    public async Task<RestoreCheck> StageAsync(string backupId, CancellationToken cancellationToken = default)
    {
        var check = await CheckAsync(backupId, cancellationToken).ConfigureAwait(false);
        if (!check.CanRestore) return check;

        var dir = BundlePath(backupId)!;
        var manifest = ReadManifest(dir)!;
        var temp = Path.Combine(DataDir, PendingFileName + ".tmp");
        File.Copy(Path.Combine(dir, manifest.Sqlite.FileName), temp, overwrite: true);
        File.Move(temp, Path.Combine(DataDir, PendingFileName), overwrite: true);
        await File.WriteAllTextAsync(Path.Combine(DataDir, PendingMarkerName),
            JsonSerializer.Serialize(new PendingRestore(backupId, _time.GetUtcNow()), Json), cancellationToken).ConfigureAwait(false);
        _logger.LogWarning("Backup {BackupId} staged for restore at the next start", backupId);
        return check;
    }

    /// <inheritdoc />
    public PendingRestore? Pending()
    {
        var marker = Path.Combine(DataDir, PendingMarkerName);
        if (!File.Exists(Path.Combine(DataDir, PendingFileName)) || !File.Exists(marker)) return null;
        try
        {
            return JsonSerializer.Deserialize<PendingRestore>(File.ReadAllText(marker), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public bool CancelPending()
    {
        var had = Pending() is not null;
        File.Delete(Path.Combine(DataDir, PendingFileName));
        File.Delete(Path.Combine(DataDir, PendingMarkerName));
        return had;
    }

    /// <summary>
    /// At start, before the first connection: moves a staged restore into place. The database it replaces — with its
    /// write-ahead log, which would otherwise be replayed into the restored file — is kept beside it, never deleted.
    /// </summary>
    /// <returns>The kept file's path, or null when nothing was staged.</returns>
    public static string? ApplyPendingRestore(string dataDirectory, DateTimeOffset now, ILogger logger)
    {
        var pending = Path.Combine(dataDirectory, PendingFileName);
        if (!File.Exists(pending)) return null;

        var live = Path.Combine(dataDirectory, ServiceHubDataDirectory.DatabaseFileName);
        var kept = $"{live}.before-restore-{now:yyyyMMdd-HHmmss}";
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            if (File.Exists(live + suffix)) File.Move(live + suffix, kept + suffix);
        }

        File.Move(pending, live);
        File.Delete(Path.Combine(dataDirectory, PendingMarkerName));
        logger.LogWarning("Restored the staged backup. The database it replaced is kept at {Kept}", kept);
        return kept;
    }

    private async Task<RestoreCheckItem> VerifyChainsAsync(string snapshot, CancellationToken cancellationToken)
    {
        const string Name = "Its evidence ledger verifies";
        try
        {
            var options = new DbContextOptionsBuilder<ServiceHubDbContext>()
                .UseSqlite(new SqliteConnectionStringBuilder { DataSource = snapshot, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString)
                .Options;
            await using var db = new ServiceHubDbContext(options);
            var events = await db.RecoveryEvents.AsNoTracking().OrderBy(e => e.OwnerId).ThenBy(e => e.Seq).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (var owner in events.GroupBy(e => e.OwnerId))
            {
                var result = RecoveryChainVerifier.Verify(owner.Key, owner.ToList());
                if (!result.IsValid)
                {
                    return new(Name, false, $"The chain breaks at event {result.FirstDivergentSeq}: {result.Reason} A restore would bring back evidence that cannot be trusted.");
                }
            }

            return new(Name, true, events.Count == 0 ? "It holds no ledger events yet — nothing to verify." : $"{events.Count:N0} events, every chain unbroken.");
        }
        catch (Exception ex) when (ex is SqliteException or InvalidOperationException or FormatException)
        {
            _logger.LogWarning(ex, "Could not read the ledger in backup {Snapshot}", snapshot);
            return new(Name, false, "Its ledger could not be read, so it cannot be verified.");
        }
    }

    private static BackupManifest? ReadManifest(string dir)
    {
        var path = Path.Combine(dir, "manifest.json");
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<BackupManifest>(File.ReadAllText(path), Json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Sha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }

    private static SqliteConnection Open(string path) =>
        new(new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ConnectionString);

    private static async Task<string> ScalarAsync(string path, string sql, CancellationToken cancellationToken)
    {
        await using var connection = Open(path);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false))?.ToString() ?? "no result";
    }

    private static async Task<List<string>?> MigrationsAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = Open(path);
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT MigrationId FROM __EFMigrationsHistory ORDER BY MigrationId;";
            await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var ids = new List<string>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false)) ids.Add(reader.GetString(0));
            return ids;
        }
        catch (SqliteException)
        {
            return null;
        }
    }
}
