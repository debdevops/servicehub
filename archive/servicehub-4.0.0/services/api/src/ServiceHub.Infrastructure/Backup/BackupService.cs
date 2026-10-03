using System.Reflection;
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
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Backup;

/// <summary>
/// Creates timestamped backup bundles containing a consistent SQLite snapshot (via
/// <c>VACUUM INTO</c>), an integrity check of that snapshot, an independent copy of the
/// namespace JSON store, and a manifest (roadmap F2).
/// </summary>
/// <remarks>
/// The SQLite snapshot and the namespace JSON copy are captured independently — not inside a
/// single cross-store transaction. <c>VACUUM INTO</c> gives the SQLite file a well-defined
/// consistent point (the instant it completes); the namespace JSON file is copied as a single
/// atomic file operation but at a different, nearby instant. See docs/BACKUP-RESTORE.md.
/// </remarks>
public sealed class BackupService : IBackupService
{
    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private const string SqliteFileName = "servicehub-dlq.db";
    private const string NamespaceStoreFileName = "servicehub-namespaces.json";
    private const string ManifestFileName = "manifest.json";

    private readonly DlqDbContext _dbContext;
    private readonly IConnectionStringProtector _connectionStringProtector;
    private readonly IConfiguration _configuration;
    private readonly BackupOptions _options;
    private readonly ILogger<BackupService> _logger;

    /// <summary>Initializes a new instance of the <see cref="BackupService"/> class.</summary>
    public BackupService(
        DlqDbContext dbContext,
        IConnectionStringProtector connectionStringProtector,
        IConfiguration configuration,
        IOptions<BackupOptions> options,
        ILogger<BackupService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result<BackupManifest>> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        var backupRoot = ResolveBackupRoot();
        Directory.CreateDirectory(backupRoot);

        var bundleDir = CreateUniqueBundleDirectory(backupRoot, out var backupId);

        try
        {
            // Absolute path: VACUUM INTO resolves a relative destination against SQLite's own
            // notion of the current directory, which need not match this process's, so a
            // relative path here could silently write the snapshot somewhere unexpected.
            var sqlitePath = Path.GetFullPath(Path.Combine(bundleDir, SqliteFileName));
            await _dbContext.Database
                .ExecuteSqlInterpolatedAsync($"VACUUM INTO {sqlitePath}", cancellationToken)
                .ConfigureAwait(false);

            var integrityCheck = await RunIntegrityCheckAsync(sqlitePath, cancellationToken).ConfigureAwait(false);
            if (!string.Equals(integrityCheck, "ok", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Backup snapshot failed integrity check ({Result}); discarding bundle {BackupId}",
                    integrityCheck, backupId);
                TryDeleteDirectory(bundleDir);
                return Result.Failure<BackupManifest>(Error.Internal(
                    ErrorCodes.Backup.IntegrityCheckFailed,
                    $"SQLite snapshot failed integrity check: {integrityCheck}"));
            }

            var sqliteInfo = BuildFileInfo(sqlitePath, SqliteFileName);
            var namespaceStoreInfo = CopyNamespaceStore(bundleDir);

            var manifest = new BackupManifest
            {
                BackupId = backupId,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                ServiceHubVersion = ResolveServiceHubVersion(),
                Sqlite = sqliteInfo,
                NamespaceStore = namespaceStoreInfo,
                IntegrityCheck = integrityCheck,
                EncryptionKeyFingerprint = _connectionStringProtector.GetKeyFingerprint(),
                ConsistencyNote =
                    "The SQLite snapshot and the namespace JSON store were captured independently, " +
                    "not as a single atomic transaction across both stores. The SQLite snapshot is " +
                    "internally consistent as of its VACUUM INTO completion time; the namespace JSON " +
                    "file is copied as a single atomic file operation at a separate, nearby instant."
            };

            var manifestPath = Path.Combine(bundleDir, ManifestFileName);
            await File.WriteAllTextAsync(
                manifestPath,
                JsonSerializer.Serialize(manifest, ManifestJsonOptions),
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Backup {BackupId} created: sqlite={SqliteBytes}B, namespaceStore={NamespaceStorePresent}, integrity={Integrity}",
                backupId, sqliteInfo.SizeBytes, namespaceStoreInfo is not null, integrityCheck);

            ApplyRetention(backupRoot, cancellationToken);

            return Result.Success(manifest);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SqliteException or DbUpdateException)
        {
            _logger.LogError(ex, "Backup creation failed for bundle {BackupId}", backupId);
            TryDeleteDirectory(bundleDir);
            return Result.Failure<BackupManifest>(Error.Internal(
                ErrorCodes.Backup.CreateFailed,
                $"Backup creation failed: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<BackupSummary>>> ListBackupsAsync(CancellationToken cancellationToken = default)
    {
        var backupRoot = ResolveBackupRoot();
        if (!Directory.Exists(backupRoot))
        {
            return Task.FromResult(Result.Success<IReadOnlyList<BackupSummary>>([]));
        }

        try
        {
            var summaries = new List<BackupSummary>();

            foreach (var dir in Directory.EnumerateDirectories(backupRoot))
            {
                var manifestPath = Path.Combine(dir, ManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                var manifest = JsonSerializer.Deserialize<BackupManifest>(
                    File.ReadAllText(manifestPath), ManifestJsonOptions);
                if (manifest is null)
                {
                    continue;
                }

                var totalBytes = manifest.Sqlite.SizeBytes + (manifest.NamespaceStore?.SizeBytes ?? 0);
                summaries.Add(new BackupSummary
                {
                    BackupId = manifest.BackupId,
                    CreatedAtUtc = manifest.CreatedAtUtc,
                    TotalSizeBytes = totalBytes,
                    IntegrityCheck = manifest.IntegrityCheck,
                    NamespaceStorePresent = manifest.NamespaceStore is not null
                });
            }

            summaries.Sort((a, b) => b.CreatedAtUtc.CompareTo(a.CreatedAtUtc));
            return Task.FromResult(Result.Success<IReadOnlyList<BackupSummary>>(summaries));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogError(ex, "Failed to list backups under {BackupRoot}", backupRoot);
            return Task.FromResult(Result.Failure<IReadOnlyList<BackupSummary>>(Error.Internal(
                ErrorCodes.Backup.ListFailed,
                $"Failed to list backups: {ex.Message}")));
        }
    }

    private string ResolveBackupRoot()
    {
        var configured = _options.BackupDirectory;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var dataDir = _configuration["DlqDatabase:DataDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        return Path.Combine(dataDir, "backups");
    }

    /// <summary>
    /// Same <c>NamespaceRepository:DataDirectory</c> resolution
    /// <see cref="ServiceHub.Infrastructure.Persistence.InMemory.InMemoryNamespaceRepository"/>
    /// itself uses, duplicated here rather than shared — the two already read the same key
    /// independently (see roadmap F2 research), and this class must not take a hard dependency
    /// on that MVP-only repository implementation.
    /// </summary>
    private string ResolveNamespaceStorePath()
    {
        var dataDir = _configuration["NamespaceRepository:DataDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "data");
        return Path.Combine(dataDir, NamespaceStoreFileName);
    }

    private static string CreateUniqueBundleDirectory(string backupRoot, out string backupId)
    {
        var baseId = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss'Z'");
        var candidate = baseId;
        var suffix = 1;

        while (Directory.Exists(Path.Combine(backupRoot, candidate)))
        {
            candidate = $"{baseId}-{suffix++}";
        }

        var bundleDir = Path.Combine(backupRoot, candidate);
        Directory.CreateDirectory(bundleDir);
        backupId = candidate;
        return bundleDir;
    }

    private static async Task<string> RunIntegrityCheckAsync(string sqlitePath, CancellationToken cancellationToken)
    {
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = sqlitePath,
            Mode = SqliteOpenMode.ReadOnly
        };

        await using var connection = new SqliteConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";

        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return reader.GetString(0);
        }

        return "no result";
    }

    private BackupFileInfo? CopyNamespaceStore(string bundleDir)
    {
        // Post-M2 (namespace store migrated to SQLite), servicehub-namespaces.json has been
        // renamed to servicehub-namespaces.json.migrated and this File.Exists check on the exact,
        // literal active filename already returns false here — no separate detection change was
        // needed, verified against CreateBackupAsync_NoNamespaceStore_CreatesSqliteSnapshotWithManifest.
        var sourcePath = ResolveNamespaceStorePath();
        if (!File.Exists(sourcePath))
        {
            return null;
        }

        var destPath = Path.Combine(bundleDir, NamespaceStoreFileName);

        // A plain copy is safe here even while the live repository may be mid-write: the source
        // is only ever replaced via write-temp-then-rename (InMemoryNamespaceRepository.SaveToDisk),
        // so a reader of the final path always sees either the previous complete file or the new
        // complete file, never a partial write.
        File.Copy(sourcePath, destPath, overwrite: true);

        return BuildFileInfo(destPath, NamespaceStoreFileName);
    }

    private static BackupFileInfo BuildFileInfo(string path, string fileName)
    {
        var bytes = File.ReadAllBytes(path);
        var hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
        return new BackupFileInfo
        {
            FileName = fileName,
            SizeBytes = bytes.LongLength,
            Sha256 = hash
        };
    }

    private void ApplyRetention(string backupRoot, CancellationToken cancellationToken)
    {
        var retentionCount = Math.Max(1, _options.RetentionCount);

        var bundleDirs = Directory.EnumerateDirectories(backupRoot)
            .Where(dir => File.Exists(Path.Combine(dir, ManifestFileName)))
            .OrderByDescending(dir => Path.GetFileName(dir), StringComparer.Ordinal)
            .ToList();

        foreach (var stale in bundleDirs.Skip(retentionCount))
        {
            cancellationToken.ThrowIfCancellationRequested();
            _logger.LogInformation("Deleting backup bundle beyond retention: {Path}", stale);
            TryDeleteDirectory(stale);
        }
    }

    private void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Failed to delete backup bundle directory {Path}", path);
        }
    }

    private static string ResolveServiceHubVersion()
    {
        var assembly = typeof(BackupService).Assembly;
        var informationalVersion = assembly
            .GetCustomAttributes(typeof(AssemblyInformationalVersionAttribute), false)
            .OfType<AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()?.InformationalVersion;

        return informationalVersion ?? assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
