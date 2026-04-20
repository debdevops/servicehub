using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Persistence.InMemory;

/// <summary>
/// Thread-safe in-memory implementation of the namespace repository.
/// Intended for development and MVP purposes only.
/// </summary>
public sealed class InMemoryNamespaceRepository : INamespaceRepository
{
    private readonly ConcurrentDictionary<Guid, Namespace> _namespaces = new();
    private readonly ILogger<InMemoryNamespaceRepository> _logger;
    private readonly string _storagePath;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="InMemoryNamespaceRepository"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configuration">The application configuration.</param>
    public InMemoryNamespaceRepository(
        ILogger<InMemoryNamespaceRepository> logger,
        IConfiguration configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        var rawDataDir = configuration["NamespaceRepository:DataDirectory"]
            ?? Path.Combine(AppContext.BaseDirectory, "data");

        // Resolve to absolute path and verify it doesn't escape outside the application root.
        // This guards against path-traversal in the DataDirectory configuration value,
        // e.g. "../../etc" supplied via an environment variable.
        var resolvedDataDir = Path.GetFullPath(rawDataDir);
        var appBaseDir = Path.GetFullPath(AppContext.BaseDirectory);

        // Allow paths under the app base OR common hosting directories (e.g. /home/data on Azure App Service)
        if (!resolvedDataDir.StartsWith(appBaseDir, StringComparison.OrdinalIgnoreCase)
            && !resolvedDataDir.StartsWith("/home", StringComparison.OrdinalIgnoreCase)
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
    public Task<Result<Namespace>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return Task.FromResult(Result.Failure<Namespace>(Error.Validation(
                ErrorCodes.Namespace.NotFound,
                "Namespace ID cannot be empty.")));
        }

        if (_namespaces.TryGetValue(id, out var ns))
        {
            _logger.LogDebug("Retrieved namespace {NamespaceId} from in-memory store", id);
            return Task.FromResult(Result.Success(ns));
        }

        _logger.LogDebug("Namespace {NamespaceId} not found in in-memory store", id);
        return Task.FromResult(Result.Failure<Namespace>(Error.NotFound(
            ErrorCodes.Namespace.NotFound,
            $"Namespace with ID '{id}' was not found.")));
    }

    /// <inheritdoc/>
    public Task<Result<Namespace>> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(Result.Failure<Namespace>(Error.Validation(
                ErrorCodes.Namespace.NameRequired,
                "Namespace name is required.")));
        }

        var normalizedName = name.Trim().ToLowerInvariant();
        var ns = _namespaces.Values.FirstOrDefault(n =>
            n.Name.Equals(normalizedName, StringComparison.OrdinalIgnoreCase));

        if (ns is not null)
        {
            _logger.LogDebug("Retrieved namespace {NamespaceName} from in-memory store", LogRedactor.SanitiseForLog(name));
            return Task.FromResult(Result.Success(ns));
        }

        _logger.LogDebug("Namespace {NamespaceName} not found in in-memory store", LogRedactor.SanitiseForLog(name));
        return Task.FromResult(Result.Failure<Namespace>(Error.NotFound(
            ErrorCodes.Namespace.NotFound,
            $"Namespace with name '{name}' was not found.")));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Namespace>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var namespaces = _namespaces.Values.ToList();
        _logger.LogDebug("Retrieved {Count} namespaces from in-memory store", namespaces.Count);
        return Task.FromResult(Result.Success<IReadOnlyList<Namespace>>(namespaces));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Namespace>>> GetByOwnerAsync(string ownerId, CancellationToken cancellationToken = default)
    {
        // SPA owner also owns legacy namespaces that pre-date the OwnerId field
        // (they were written without an OwnerId and deserialise to the default value).
        var namespaces = _namespaces.Values
            .Where(n => string.Equals(n.OwnerId, ownerId, StringComparison.Ordinal))
            .ToList();

        _logger.LogDebug(
            "Retrieved {Count} namespaces for owner {OwnerId}",
            namespaces.Count,
            ownerId);

        return Task.FromResult(Result.Success<IReadOnlyList<Namespace>>(namespaces));
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Namespace>>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var activeNamespaces = _namespaces.Values
            .Where(n => n.IsActive)
            .ToList();

        _logger.LogDebug("Retrieved {Count} active namespaces from in-memory store", activeNamespaces.Count);
        return Task.FromResult(Result.Success<IReadOnlyList<Namespace>>(activeNamespaces));
    }

    /// <inheritdoc/>
    public Task<Result> AddAsync(Namespace @namespace, CancellationToken cancellationToken = default)
    {
        if (@namespace is null)
        {
            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.NotFound,
                "Namespace cannot be null.")));
        }

        // Check for duplicate name within the same tenant
        var existingByName = _namespaces.Values.FirstOrDefault(n =>
            n.Name.Equals(@namespace.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(n.OwnerId, @namespace.OwnerId, StringComparison.Ordinal));

        if (existingByName is not null)
        {
            _logger.LogWarning(
                "Attempted to add namespace with duplicate name {NamespaceName}",
                LogRedactor.SanitiseForLog(@namespace.Name));

            return Task.FromResult(Result.Failure(Error.Conflict(
                ErrorCodes.Namespace.AlreadyExists,
                $"A namespace with the name '{@namespace.Name}' already exists.")));
        }

        if (_namespaces.TryAdd(@namespace.Id, @namespace))
        {
            SaveToDisk();

            _logger.LogInformation(
                "Added namespace {NamespaceId} ({NamespaceName}) to in-memory store",
                @namespace.Id,
                LogRedactor.SanitiseForLog(@namespace.Name));

            return Task.FromResult(Result.Success());
        }

        return Task.FromResult(Result.Failure(Error.Conflict(
            ErrorCodes.Namespace.AlreadyExists,
            $"A namespace with the ID '{@namespace.Id}' already exists.")));
    }

    /// <inheritdoc/>
    public Task<Result> UpdateAsync(Namespace @namespace, CancellationToken cancellationToken = default)
    {
        if (@namespace is null)
        {
            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.NotFound,
                "Namespace cannot be null.")));
        }

        if (!_namespaces.ContainsKey(@namespace.Id))
        {
            _logger.LogWarning(
                "Attempted to update non-existent namespace {NamespaceId}",
                @namespace.Id);

            return Task.FromResult(Result.Failure(Error.NotFound(
                ErrorCodes.Namespace.NotFound,
                $"Namespace with ID '{@namespace.Id}' was not found.")));
        }

        // Prevent OwnerId from being changed — immutable after creation
        var existing = _namespaces[@namespace.Id];
        if (!string.Equals(existing.OwnerId, @namespace.OwnerId, StringComparison.Ordinal))
        {
            _logger.LogWarning(
                "Attempted to change OwnerId on namespace {NamespaceId}",
                @namespace.Id);

            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.NotFound,
                "Cannot modify the owner of a namespace.")));
        }

        // Check for duplicate name (excluding current namespace) within the same tenant
        var existingByName = _namespaces.Values.FirstOrDefault(n =>
            n.Id != @namespace.Id &&
            n.Name.Equals(@namespace.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(n.OwnerId, @namespace.OwnerId, StringComparison.Ordinal));

        if (existingByName is not null)
        {
            return Task.FromResult(Result.Failure(Error.Conflict(
                ErrorCodes.Namespace.AlreadyExists,
                $"A namespace with the name '{@namespace.Name}' already exists.")));
        }

        _namespaces[@namespace.Id] = @namespace;

        SaveToDisk();

        _logger.LogInformation(
            "Updated namespace {NamespaceId} ({NamespaceName}) in in-memory store",
            @namespace.Id,
            LogRedactor.SanitiseForLog(@namespace.Name));

        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.NotFound,
                "Namespace ID cannot be empty.")));
        }

        if (_namespaces.TryRemove(id, out var removed))
        {
            SaveToDisk();

            _logger.LogInformation(
                "Deleted namespace {NamespaceId} ({NamespaceName}) from in-memory store",
                id,
                LogRedactor.SanitiseForLog(removed.Name));

            return Task.FromResult(Result.Success());
        }

        _logger.LogWarning(
            "Attempted to delete non-existent namespace {NamespaceId}",
            id);

        return Task.FromResult(Result.Failure(Error.NotFound(
            ErrorCodes.Namespace.NotFound,
            $"Namespace with ID '{id}' was not found.")));
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string name, string ownerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(false);
        }

        var exists = _namespaces.Values.Any(n =>
            n.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase) &&
            string.Equals(n.OwnerId, ownerId, StringComparison.Ordinal));

        return Task.FromResult(exists);
    }

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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load namespaces from {Path}", _storagePath);
        }
    }

    private void SaveToDisk()
    {
        try
        {
            var snapshots = _namespaces.Values
                .Select(ToSnapshot)
                .OrderBy(n => n.Name)
                .ToList();

            var json = JsonSerializer.Serialize(snapshots, JsonOptions);

            // CRITICAL FIX: Atomic write via temp file + rename.
            // File.WriteAllText truncates then writes — a crash mid-write produces a
            // zero-byte or partial file, losing ALL stored namespaces permanently.
            // File.Move is atomic on the same volume (single directory rename syscall).
            var tempPath = _storagePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _storagePath, overwrite: true);

            _logger.LogDebug("Persisted {Count} namespace(s) to {Path}", snapshots.Count, _storagePath);
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
            ConnectionStringHash = ns.ConnectionStringHash,
        };

    private Namespace? Rehydrate(NamespaceSnapshot snapshot)
    {
        try
        {
            Result<Namespace> createResult = snapshot.AuthType == ConnectionAuthType.ConnectionString
                ? Namespace.Create(
                    snapshot.Name,
                    snapshot.ConnectionString ?? string.Empty,
                    snapshot.DisplayName,
                    snapshot.Description,
                    snapshot.Environment,
                    ownerId: snapshot.OwnerId,
                    connectionStringHash: snapshot.ConnectionStringHash)
                : Namespace.CreateWithManagedIdentity(
                    snapshot.Name,
                    snapshot.AuthType,
                    snapshot.DisplayName,
                    snapshot.Description,
                    snapshot.Environment,
                    ownerId: snapshot.OwnerId);

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
        /// <summary>SHA-256 hash of the plaintext connection string for fast deduplication.</summary>
        public string? ConnectionStringHash { get; init; }
    }
}
