using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.Persistence.InMemory;

/// <summary>
/// Namespace repository backed by a JSON file on disk, so stored connections survive
/// process restarts. Intended for development and MVP purposes only.
/// </summary>
public sealed class InMemoryNamespaceRepository : InMemoryNamespaceRepositoryBase
{
    private readonly string _storagePath;
    private readonly object _saveLock = new();
    private static readonly JsonSerializerOptions JsonOptions = NamespaceJsonSnapshot.JsonOptions;

    // Fixed GUIDs the removed ServiceHub.Simulator project's SimulatorDataSeeder used to
    // register its simulated namespaces directly in this shared repository. That project is
    // gone, but namespaces it already persisted to disk remain until cleaned up below.
    private static readonly HashSet<Guid> LegacySimulatorNamespaceIds =
    [
        new Guid("a1b2c3d4-0001-0001-0001-000000000001"),
        new Guid("b2c3d4e5-0002-0002-0002-000000000002"),
        new Guid("c3d4e5f6-0003-0003-0003-000000000003"),
    ];

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryNamespaceRepository"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configuration">The application configuration.</param>
    public InMemoryNamespaceRepository(
        ILogger<InMemoryNamespaceRepository> logger,
        IConfiguration configuration)
        : base(logger)
    {
        var rawDataDir = configuration["NamespaceRepository:DataDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "data");

        // Resolve to absolute path and verify it doesn't escape outside the application root.
        // This guards against path-traversal in the DataDirectory configuration value,
        // e.g. "../../etc" supplied via an environment variable.
        var resolvedDataDir = Path.GetFullPath(rawDataDir);
        var appBaseDir = Path.GetFullPath(AppContext.BaseDirectory);

        // Allow paths under the app base OR common hosting directories.
        // /home  — Azure App Service persistent storage
        // /var   — generic Linux (e.g. /var/servicehub/data, /var/lib/...)
        // /opt   — common for installed app data on Debian/Ubuntu
        // /tmp   — test / non-persistent environments
        if (!resolvedDataDir.StartsWith(appBaseDir, StringComparison.OrdinalIgnoreCase)
            && !resolvedDataDir.StartsWith("/home", StringComparison.OrdinalIgnoreCase)
            && !resolvedDataDir.StartsWith("/var", StringComparison.OrdinalIgnoreCase)
            && !resolvedDataDir.StartsWith("/opt", StringComparison.OrdinalIgnoreCase)
            && !resolvedDataDir.StartsWith(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "DataDirectory '{Resolved}' is outside allowed paths; falling back to app base directory.",
                resolvedDataDir);
            resolvedDataDir = Path.Combine(appBaseDir, "data");
        }

        Directory.CreateDirectory(resolvedDataDir);
        _storagePath = Path.Combine(resolvedDataDir, "servicehub-namespaces.json");

        LoadFromDisk();
    }

    /// <inheritdoc/>
    protected override void OnMutated() => SaveToDisk();

    private void LoadFromDisk()
    {
        try
        {
            if (!File.Exists(_storagePath))
            {
                _logger.LogInformation("Namespace storage file not found at {Path}, starting empty", _storagePath);
                return;
            }

            var json = File.ReadAllText(_storagePath);
            var snapshots = JsonSerializer.Deserialize<List<NamespaceJsonSnapshot.Entry>>(json, JsonOptions) ?? [];

            var loaded = 0;
            foreach (var snapshot in snapshots)
            {
                var ns = NamespaceJsonSnapshot.Rehydrate(snapshot, _logger);
                if (ns is null)
                {
                    continue;
                }

                _namespaces[ns.Id] = ns;
                loaded++;
            }

            _logger.LogInformation("Loaded {Count} namespace(s) from {Path}", loaded, _storagePath);

            RemoveLegacySimulatorNamespaces();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load namespaces from {Path}", _storagePath);
        }
    }

    /// <summary>
    /// One-time startup migration: purges namespaces left behind by the removed
    /// ServiceHub.Simulator feature and, if any were found, immediately re-persists the
    /// cleaned store. Runs only against what was just loaded from disk, so it never touches
    /// namespaces created afterward even if their name happens to start with "sim-".
    /// </summary>
    private void RemoveLegacySimulatorNamespaces()
    {
        var legacyIds = _namespaces.Values
            .Where(IsLegacySimulatorNamespace)
            .Select(ns => ns.Id)
            .ToList();

        if (legacyIds.Count == 0)
        {
            return;
        }

        foreach (var id in legacyIds)
        {
            _namespaces.TryRemove(id, out _);
        }

        _logger.LogInformation(
            "Removed {Count} legacy simulator namespace(s) left over from the removed Simulator feature",
            legacyIds.Count);

        SaveToDisk();
    }

    private static bool IsLegacySimulatorNamespace(Namespace ns) =>
        LegacySimulatorNamespaceIds.Contains(ns.Id)
        || (ns.DisplayName?.StartsWith("Simulated", StringComparison.Ordinal) ?? false)
        || ns.Name.StartsWith("sim-", StringComparison.Ordinal);

    private void SaveToDisk()
    {
        try
        {
            // Guards the whole serialise → write → rename sequence. Without this, two
            // concurrent writers both targeting the same fixed temp filename could interleave
            // their writes on that shared file before either renamed — corrupting or
            // truncating the one file every stored credential lives in. The lock is safe here
            // because the method is synchronous and the section below never awaits.
            lock (_saveLock)
            {
                var snapshots = _namespaces.Values
                    .Select(ToSnapshot)
                    .OrderBy(n => n.Name)
                    .ToList();

                var json = JsonSerializer.Serialize(snapshots, JsonOptions);
                var bytes = Encoding.UTF8.GetBytes(json);

                // Atomic write via a unique-per-call temp file + rename. The unique name is
                // defence in depth (still correct if this method is ever made async and the
                // lock no longer applies). The temp file is flushed to the physical device
                // (flushToDisk: true — fsync on Linux, FlushFileBuffers on Windows) before the
                // rename, so an unclean shutdown between write and rename cannot leave a
                // valid-looking but empty or truncated file at the destination. File.Move is
                // then a single atomic rename syscall on the same volume.
                var tempPath = $"{_storagePath}.{Guid.NewGuid():N}.tmp";
                try
                {
                    using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        stream.Write(bytes, 0, bytes.Length);
                        stream.Flush(flushToDisk: true);
                    }

                    // Restrict to owner-only before the rename, never after: this file holds every
                    // stored connection string (encrypted, but still credential material), and
                    // default permissions on a typical host leave it group- and world-readable.
                    // Setting the mode on the temp file means the destination is never briefly
                    // readable by other local accounts.
                    RestrictToOwnerOnly(tempPath);

                    File.Move(tempPath, _storagePath, overwrite: true);
                }
                catch
                {
                    // Don't leave a .{guid}.tmp file behind holding a full copy of the namespace
                    // store — they accumulate silently and each one is credential material.
                    TryDeleteTempFile(tempPath);
                    throw;
                }

                _logger.LogDebug("Persisted {Count} namespace(s) to {Path}", snapshots.Count, _storagePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist namespaces to {Path}", _storagePath);
        }
    }

    /// <summary>
    /// Restricts a file to owner read/write only (0600 on Unix). No-ops on Windows, where the
    /// inherited directory ACL governs access and there is no direct equivalent to set here.
    /// Failure is logged and swallowed: tightening permissions is defence in depth, and a
    /// filesystem that refuses it (a mounted volume with fixed permissions, for example) must
    /// not stop namespaces from being persisted at all.
    /// </summary>
    private void RestrictToOwnerOnly(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not restrict permissions on the namespace store file; it may be readable "
                + "by other local accounts on this host");
        }
    }

    private void TryDeleteTempFile(string tempPath)
    {
        try
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to remove the temporary namespace store file left by a failed write");
        }
    }

    private static NamespaceJsonSnapshot.Entry ToSnapshot(Namespace ns) => NamespaceJsonSnapshot.ToSnapshot(ns);
}
