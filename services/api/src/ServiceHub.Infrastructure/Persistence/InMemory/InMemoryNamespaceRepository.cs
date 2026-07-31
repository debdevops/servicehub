using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Persistence.InMemory;

/// <summary>
/// Namespace repository backed by a JSON file on disk, so stored connections survive
/// process restarts. Intended for development and MVP purposes only.
/// </summary>
public sealed class InMemoryNamespaceRepository : InMemoryNamespaceRepositoryBase
{
    private readonly string _storagePath;
    private readonly object _saveLock = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

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
            var snapshots = JsonSerializer.Deserialize<List<NamespaceSnapshot>>(json, JsonOptions) ?? [];

            var loaded = 0;
            foreach (var snapshot in snapshots)
            {
                var ns = Rehydrate(snapshot);
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
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(tempPath, _storagePath, overwrite: true);

                _logger.LogDebug("Persisted {Count} namespace(s) to {Path}", snapshots.Count, _storagePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist namespaces to {Path}", _storagePath);
        }
    }

    private static NamespaceSnapshot ToSnapshot(Namespace ns)
        => new()
        {
            Id = ns.Id,
            Name = ns.Name,
            DisplayName = ns.DisplayName,
            Description = ns.Description,
            ConnectionString = ns.ConnectionString,
            AuthType = ns.AuthType,
            IsActive = ns.IsActive,
            CreatedAt = ns.CreatedAt,
            ModifiedAt = ns.ModifiedAt,
            LastConnectionTestAt = ns.LastConnectionTestAt,
            LastConnectionTestSucceeded = ns.LastConnectionTestSucceeded,
            HasListenPermission = ns.HasListenPermission,
            HasSendPermission = ns.HasSendPermission,
            HasManagePermission = ns.HasManagePermission,
            Environment = ns.Environment,
            OwnerId = ns.OwnerId,
            SharedWithOwnerIds = ns.SharedWithOwnerIds.Count > 0 ? [.. ns.SharedWithOwnerIds] : null,
            ConnectionStringHash = ns.ConnectionStringHash,
            Provider = ns.Provider,
            AwsRegion = ns.AwsRegion,
            GcpProjectId = ns.GcpProjectId,
        };

    private Namespace? Rehydrate(NamespaceSnapshot snapshot)
    {
        try
        {
            // Dispatch on stored credentials, not AuthType: AWS/GCP namespaces persist with
            // AwsAccessKey/GcpServiceAccount auth types but still carry a connection string,
            // and CreateWithManagedIdentity would reject (and drop) them on reload.
            Result<Namespace> createResult = !string.IsNullOrWhiteSpace(snapshot.ConnectionString)
                ? Namespace.Create(
                    snapshot.Name,
                    snapshot.ConnectionString ?? string.Empty,
                    snapshot.DisplayName,
                    snapshot.Description,
                    snapshot.Environment,
                    provider: snapshot.Provider,
                    ownerId: snapshot.OwnerId,
                    connectionStringHash: snapshot.ConnectionStringHash,
                    awsRegion: snapshot.AwsRegion,
                    gcpProjectId: snapshot.GcpProjectId)
                : Namespace.CreateWithManagedIdentity(
                    snapshot.Name,
                    snapshot.AuthType,
                    snapshot.DisplayName,
                    snapshot.Description,
                    snapshot.Environment,
                    provider: snapshot.Provider,
                    ownerId: snapshot.OwnerId,
                    awsRegion: snapshot.AwsRegion,
                    gcpProjectId: snapshot.GcpProjectId);

            if (createResult.IsFailure)
            {
                _logger.LogWarning(
                    "Skipping persisted namespace {Name} due to validation failure while rehydrating",
                    LogRedactor.SanitiseForLog(snapshot.Name));
                return null;
            }

            var ns = createResult.Value;

            SetPrivateProperty(ns, nameof(Namespace.Id), snapshot.Id);
            SetPrivateProperty(ns, nameof(Namespace.CreatedAt), snapshot.CreatedAt);
            SetPrivateProperty(ns, nameof(Namespace.ModifiedAt), snapshot.ModifiedAt);
            SetPrivateProperty(ns, nameof(Namespace.LastConnectionTestAt), snapshot.LastConnectionTestAt);
            SetPrivateProperty(ns, nameof(Namespace.LastConnectionTestSucceeded), snapshot.LastConnectionTestSucceeded);
            SetPrivateProperty(ns, nameof(Namespace.HasListenPermission), snapshot.HasListenPermission);
            SetPrivateProperty(ns, nameof(Namespace.HasSendPermission), snapshot.HasSendPermission);
            SetPrivateProperty(ns, nameof(Namespace.HasManagePermission), snapshot.HasManagePermission);
            SetPrivateProperty(ns, nameof(Namespace.Environment), snapshot.Environment);
            SetPrivateProperty(ns, nameof(Namespace.SharedWithOwnerIds), (IReadOnlyList<string>)(snapshot.SharedWithOwnerIds ?? []));

            if (!snapshot.IsActive)
            {
                ns.Deactivate();
            }

            return ns;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rehydrate persisted namespace {Name}", LogRedactor.SanitiseForLog(snapshot.Name));
            return null;
        }
    }

    private static void SetPrivateProperty<T>(Namespace target, string propertyName, T value)
    {
        var property = typeof(Namespace).GetProperty(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        property?.SetValue(target, value);
    }

    private sealed class NamespaceSnapshot
    {
        public Guid Id { get; init; }
        public string Name { get; init; } = string.Empty;
        public string? DisplayName { get; init; }
        public string? Description { get; init; }
        public string? ConnectionString { get; init; }
        public ConnectionAuthType AuthType { get; init; }
        public bool IsActive { get; init; }
        public DateTimeOffset CreatedAt { get; init; }
        public DateTimeOffset? ModifiedAt { get; init; }
        public DateTimeOffset? LastConnectionTestAt { get; init; }
        public bool? LastConnectionTestSucceeded { get; init; }
        public bool HasListenPermission { get; init; }
        public bool HasSendPermission { get; init; }
        public bool HasManagePermission { get; init; }
        public EnvironmentType Environment { get; init; }
        /// <summary>
        /// Owner identifier for tenant isolation. Defaults to the SPA owner so that
        /// namespaces written before this field existed remain visible to the instance admin.
        /// </summary>
        public string OwnerId { get; init; } = Namespace.SpaOwnerId;
        /// <summary>
        /// Additional owner IDs this namespace is shared with. Null/absent on files written
        /// before sharing existed — deserialises to null and is normalised to an empty list on
        /// rehydration, so older snapshot files load unaffected.
        /// </summary>
        public List<string>? SharedWithOwnerIds { get; init; }
        /// <summary>SHA-256 hash of the plaintext connection string for fast deduplication.</summary>
        public string? ConnectionStringHash { get; init; }
        /// <summary>Cloud provider (Azure, AWS, GCP). Defaults to Azure for backward compatibility.</summary>
        public CloudProviderType Provider { get; init; } = CloudProviderType.Azure;
        /// <summary>AWS region identifier. Null for non-AWS namespaces.</summary>
        public string? AwsRegion { get; init; }
        /// <summary>GCP project identifier. Null for non-GCP namespaces.</summary>
        public string? GcpProjectId { get; init; }
    }
}
