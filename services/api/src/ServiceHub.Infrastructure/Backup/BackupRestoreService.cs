using System.IO.Compression;
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
using ServiceHub.Core.Security;
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

    /// <summary>Marks "start fresh" for the next start. Holds a <see cref="PendingRestore"/> of kind <c>fresh</c>.</summary>
    internal const string FreshMarkerName = ServiceHubDataDirectory.DatabaseFileName + ".fresh-pending.json";

    private const string ManifestFileName = "manifest.json";
    private const long MaxManifestBytes = 1_048_576;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true };

    private readonly ServiceHubDbContext _db;
    private readonly IConnectionStringProtector _protector;
    private readonly IBackupService _backups;
    private readonly IConfiguration _configuration;
    private readonly BackupOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<BackupRestoreService> _logger;

    /// <summary>Creates the service.</summary>
    public BackupRestoreService(
        ServiceHubDbContext db, IConnectionStringProtector protector, IBackupService backups, IConfiguration configuration, IOptions<BackupOptions> options,
        ILogger<BackupRestoreService> logger, TimeProvider? time = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _backups = backups ?? throw new ArgumentNullException(nameof(backups));
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

    /// <summary>
    /// The snapshot file a manifest names, or null when the name is not a plain file name inside the bundle. A manifest sits in a folder
    /// anyone with write access to the backup directory can edit, so its file name is never joined to a path unchecked: an absolute path or
    /// a `..` would make the restore check read — and stage — some other SQLite file on the machine.
    /// </summary>
    internal static string? SnapshotPath(string bundleDir, BackupManifest manifest)
    {
        var name = manifest.Sqlite.FileName;
        if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or "..")
        {
            return null;
        }

        // A lexically plain name can still be a symlink (or other reparse point) to a database elsewhere; File.Exists, hashing and
        // File.Copy would all follow it. Neither the snapshot nor the bundle folder may be one.
        var path = Path.Combine(bundleDir, name);
        return IsLink(path) || IsLink(bundleDir) ? null : path;
    }

    private static bool IsLink(string path)
    {
        FileSystemInfo info = Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
        return info.LinkTarget is not null || (info.Exists && info.Attributes.HasFlag(FileAttributes.ReparsePoint));
    }

    /// <inheritdoc />
    public Task<RestoreCheck> CheckAsync(string backupId, CancellationToken cancellationToken = default) =>
        CheckCoreAsync(backupId, stageTo: null, cancellationToken);

    /// <summary>
    /// Runs every restore check on one private, checksum-verified copy of the snapshot, using the one manifest read at the start. When
    /// <paramref name="stageTo"/> is given and every check passes, that very copy is moved there, so what is staged is exactly the bytes
    /// that were checked — nothing is re-read from the backup folder afterwards, where it could have been swapped.
    /// </summary>
    private async Task<RestoreCheck> CheckCoreAsync(string backupId, string? stageTo, CancellationToken cancellationToken)
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

        var snapshot = SnapshotPath(dir, manifest);
        if (snapshot is null)
        {
            checks.Add(new("The backup is well-formed", false, "Its manifest names a database file outside the backup's own folder, so it is not trusted."));
            return Done();
        }

        // Every check runs on a private copy that was verified against the manifest's checksum while it was being copied, never on the
        // shared path: that path can be swapped (e.g. for a link) between any two reads, so checking it in place proves nothing about
        // the file that is later staged.
        var work = Path.Combine(DataDir, $"restore-check-{Guid.NewGuid():N}.tmp");
        var intact = false;
        try
        {
            CopyVerified(snapshot, work, manifest.Sqlite.Sha256);
            intact = true;
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            // fall through: reported below as not intact
        }

        checks.Add(new("The file is the one that was backed up", intact,
            intact ? "Its checksum matches the manifest." : "Its checksum does not match the manifest — it was changed or damaged after the backup."));
        if (!intact) return Done();

        try
        {
            snapshot = work;

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
            var result = Done();
            if (stageTo is not null && result.CanRestore)
            {
                File.Move(work, stageTo, overwrite: true);
            }

            return result;
        }
        finally
        {
            File.Delete(work);
        }
    }

    /// <inheritdoc />
    public async Task<RestoreCheck> StageAsync(string backupId, CancellationToken cancellationToken = default)
    {
        var temp = Path.Combine(DataDir, PendingFileName + ".tmp");
        var check = await CheckCoreAsync(backupId, temp, cancellationToken).ConfigureAwait(false);
        if (!check.CanRestore) return check;

        // A restore replaces everything recorded since the backup. Back the current state up first, so the restore can itself be undone
        // from the list — not only from the file set aside at the next start. If that cannot be done, nothing is staged.
        var safety = await _backups.CreateBackupAsync(cancellationToken).ConfigureAwait(false);
        if (safety.IsFailure)
        {
            File.Delete(temp);
            return new RestoreCheck(backupId, false, [.. check.Checks, new("The current state was backed up first", false,
                "ServiceHub could not back up what it holds now, so it will not replace it. Nothing was staged.")]);
        }

        ClearStaged();
        File.Move(temp, Path.Combine(DataDir, PendingFileName), overwrite: true);
        await File.WriteAllTextAsync(Path.Combine(DataDir, PendingMarkerName),
            JsonSerializer.Serialize(new PendingRestore(backupId, _time.GetUtcNow()), Json), cancellationToken).ConfigureAwait(false);
        _logger.LogWarning("Backup {BackupId} staged for restore at the next start; the current state is backed up as {SafetyBackupId}",
            LogRedactor.SanitiseForLog(backupId), LogRedactor.SanitiseForLog(safety.Value.BackupId));
        return new RestoreCheck(backupId, true, [.. check.Checks, new("The current state was backed up first", true,
            $"What ServiceHub holds now is kept as backup {safety.Value.BackupId}, so this restore can be undone.")]);
    }

    /// <inheritdoc />
    public void StageFresh(string safetyBackupId)
    {
        ClearStaged();
        File.WriteAllText(Path.Combine(DataDir, FreshMarkerName),
            JsonSerializer.Serialize(new PendingRestore(safetyBackupId, _time.GetUtcNow(), PendingRestore.FreshKind), Json));
        _logger.LogWarning("A fresh start is staged for the next start; the current state is backed up as {BackupId}", LogRedactor.SanitiseForLog(safetyBackupId));
    }

    private void ClearStaged()
    {
        foreach (var name in new[] { PendingFileName, PendingMarkerName, FreshMarkerName })
        {
            File.Delete(Path.Combine(DataDir, name));
        }
    }

    /// <inheritdoc />
    public PendingRestore? Pending()
    {
        var fresh = Path.Combine(DataDir, FreshMarkerName);
        if (File.Exists(fresh)) return ReadMarker(fresh);

        var marker = Path.Combine(DataDir, PendingMarkerName);
        return File.Exists(Path.Combine(DataDir, PendingFileName)) && File.Exists(marker) ? ReadMarker(marker) : null;
    }

    private static PendingRestore? ReadMarker(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<PendingRestore>(File.ReadAllText(path), Json);
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
        ClearStaged();
        return had;
    }

    /// <summary>
    /// At start, before the first connection: moves a staged restore into place. The database it replaces — with its
    /// write-ahead log, which would otherwise be replayed into the restored file — is kept beside it, never deleted.
    /// </summary>
    /// <returns>The kept file's path; null when nothing was staged or there was no database to replace.</returns>
    public static string? ApplyPendingRestore(string dataDirectory, DateTimeOffset now, ILogger logger)
    {
        var pending = Path.Combine(dataDirectory, PendingFileName);
        var live = Path.Combine(dataDirectory, ServiceHubDataDirectory.DatabaseFileName);

        // A restore wins over a fresh marker: staging either clears the other, so both together means a half-finished edit, and the
        // restore is the one that names a file to bring back.
        var freshMarker = Path.Combine(dataDirectory, FreshMarkerName);
        if (!File.Exists(pending))
        {
            if (!File.Exists(freshMarker)) return null;

            // Fresh start: set the database aside (with its write-ahead log) and let the migrations make an empty one. Backups are not touched.
            var setAside = $"{live}.before-fresh-{now:yyyyMMdd-HHmmss}";
            var had = File.Exists(live);
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                if (File.Exists(live + suffix)) File.Move(live + suffix, setAside + suffix);
            }

            File.Delete(freshMarker);
            if (had) logger.LogWarning("Starting fresh. The database it set aside is kept at {Kept}", setAside);
            return had ? setAside : null;
        }

        File.Delete(freshMarker);
        var kept = $"{live}.before-restore-{now:yyyyMMdd-HHmmss}";
        var replaced = File.Exists(live);
        foreach (var suffix in new[] { "", "-wal", "-shm" })
        {
            if (File.Exists(live + suffix)) File.Move(live + suffix, kept + suffix);
        }

        File.Move(pending, live);
        File.Delete(Path.Combine(dataDirectory, PendingMarkerName));
        if (replaced)
        {
            logger.LogWarning("Restored the staged backup. The database it replaced is kept at {Kept}", kept);
            return kept;
        }

        // A fresh data directory (a new container): there was nothing to keep, so the log must not claim otherwise.
        logger.LogWarning("Restored the staged backup into an empty data directory.");
        return null;
    }

    /// <summary>
    /// Writes a backup as one portable file: its manifest and its database in a zip. This is what moves a backup to another server
    /// (or back after a fresh start on a new disk) through <see cref="ImportAsync"/> — the plain database download carries no manifest.
    /// </summary>
    /// <returns>False when the id is not a backup with a readable manifest and snapshot.</returns>
    public async Task<bool> ExportAsync(string backupId, Stream output, CancellationToken cancellationToken)
    {
        var dir = BundlePath(backupId);
        var manifest = dir is null ? null : ReadManifest(dir);
        var snapshot = dir is null || manifest is null ? null : SnapshotPath(dir, manifest);
        if (dir is null || manifest is null || snapshot is null || !File.Exists(snapshot)) return false;

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true); // `output` is a seekable temp file, so synchronous disposal is fine
        await CopyToEntryAsync(zip, Path.Combine(dir, ManifestFileName), ManifestFileName, cancellationToken).ConfigureAwait(false);
        await CopyToEntryAsync(zip, snapshot, manifest.Sqlite.FileName, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private static async Task CopyToEntryAsync(ZipArchive zip, string source, string entryName, CancellationToken cancellationToken)
    {
        var entry = zip.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var to = entry.Open();
        await using var from = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read);
        await from.CopyToAsync(to, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Takes an exported bundle into the backups folder under a new id, after proving the database in it is the one its manifest
    /// checksummed. Only the manifest and the one database file it names are read from the zip, by name and never by path, and no more
    /// bytes than the manifest declares are ever written. The result is an ordinary backup: check it and restore it like any other.
    /// </summary>
    /// <returns>The new backup id, or null with a problem saying why nothing was kept.</returns>
    public async Task<(string? BackupId, string? Problem)> ImportAsync(Stream bundle, CancellationToken cancellationToken)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(bundle, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentOutOfRangeException or IOException)
        {
            // A file too short to hold a zip's directory throws ArgumentOutOfRange from the seek, not InvalidData.
            return (null, "That file is not a ServiceHub backup. Import the .zip from a backup's Export button.");
        }

        using (zip)
        {
            var manifestEntry = zip.GetEntry(ManifestFileName);
            if (manifestEntry is null || manifestEntry.Length > MaxManifestBytes)
            {
                return (null, "That zip has no manifest.json, so it is not a ServiceHub backup.");
            }

            BackupManifest? manifest;
            try
            {
                await using var stream = manifestEntry.Open();
                manifest = await JsonSerializer.DeserializeAsync<BackupManifest>(stream, Json, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonException)
            {
                manifest = null;
            }

            var name = manifest?.Sqlite.FileName;
            if (manifest is null || string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || name is "." or "..")
            {
                return (null, "That backup's manifest is damaged, so it was not imported.");
            }

            var dbEntry = zip.GetEntry(name);
            if (dbEntry is null)
            {
                return (null, "That zip does not hold the database its manifest names.");
            }

            Directory.CreateDirectory(BackupRoot);
            var baseId = $"imported-{_time.GetUtcNow():yyyyMMdd-HHmmss'Z'}";
            var id = baseId;
            for (var n = 1; Directory.Exists(Path.Combine(BackupRoot, id)); n++) id = $"{baseId}-{n}";
            var dir = Path.Combine(BackupRoot, id);
            Directory.CreateDirectory(dir);
            try
            {
                var target = Path.Combine(dir, name);
                using (var input = dbEntry.Open())
                using (var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
                {
                    var buffer = new byte[81920];
                    long total = 0;
                    int read;
                    while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                    {
                        total += read;
                        if (total > manifest.Sqlite.SizeBytes) throw new InvalidDataException("The database is larger than its manifest says.");
                        hash.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                    }

                    if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), manifest.Sqlite.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("The database does not match its manifest's checksum.");
                    }
                }

                // The folder name is the id everywhere else (the list, download, restore), so the manifest must say the same.
                await File.WriteAllTextAsync(Path.Combine(dir, ManifestFileName),
                    JsonSerializer.Serialize(manifest with { BackupId = id }, Json), cancellationToken).ConfigureAwait(false);
                _logger.LogWarning("Imported backup {BackupId}", LogRedactor.SanitiseForLog(id));
                return (id, null);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException)
            {
                try { Directory.Delete(dir, recursive: true); } catch (IOException) { /* nothing more to do */ }
                return (null, ex is InvalidDataException
                    ? $"That backup was damaged or altered, so it was not imported. {ex.Message}"
                    : "ServiceHub could not write the imported backup, so nothing was kept.");
            }
        }
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
            _logger.LogWarning(ex, "Could not read the ledger in backup {Snapshot}", LogRedactor.SanitiseForLog(snapshot));
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

    /// <summary>
    /// Copies the snapshot through ONE open handle, hashing the bytes as they are staged, and keeps the copy only if they are the bytes the
    /// manifest checksummed. The link check and the earlier checksum read are path-based, so the file could be swapped for a link between
    /// them and this copy; reading and hashing the same handle means whatever is staged is exactly what the manifest vouches for.
    /// </summary>
    internal static void CopyVerified(string source, string destination, string expectedSha256)
    {
        try
        {
            using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var output = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256))
            {
                // Re-check on the open handle's path: a link swapped in before the open is caught here, and the hash below catches the rest.
                if (IsLink(source)) throw new InvalidOperationException("The backup's database file is a link, so it is not trusted.");

                var buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hash.AppendData(buffer, 0, read);
                    output.Write(buffer, 0, read);
                }

                if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), expectedSha256, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("The backup's database file changed while it was being staged, so nothing was restored.");
                }
            }
        }
        catch
        {
            File.Delete(destination);
            throw;
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
