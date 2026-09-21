using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Persistence.InMemory;

/// <summary>
/// Shared in-memory CRUD implementation of <see cref="INamespaceRepository"/>.
/// Subclasses decide whether mutations are persisted anywhere beyond the
/// in-process dictionary by overriding <see cref="OnMutated"/> — the default
/// is a no-op, i.e. purely ephemeral storage.
/// </summary>
public abstract class InMemoryNamespaceRepositoryBase : INamespaceRepository
{
    protected readonly ConcurrentDictionary<Guid, Namespace> _namespaces = new();
    protected readonly ILogger _logger;

    protected InMemoryNamespaceRepositoryBase(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Called after a successful Add/Update/Delete. Override to persist the
    /// mutation beyond the in-memory dictionary. Default is a no-op.
    /// </summary>
    protected virtual void OnMutated()
    {
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
    public Task<Result<IReadOnlyList<Namespace>>> GetByOwnerAsync(string ownerId, IReadOnlySet<Guid>? allowedNamespaceIds = null, CancellationToken cancellationToken = default)
    {
        // SPA owner also owns legacy namespaces that pre-date the OwnerId field
        // (they were written without an OwnerId and deserialise to the default value).
        // IsAccessibleBy also includes namespaces explicitly shared with this owner, further
        // narrowed by allowedNamespaceIds when the caller's credential carries an allow-list.
        var namespaces = _namespaces.Values
            .Where(n => n.IsAccessibleBy(ownerId, allowedNamespaceIds))
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
            OnMutated();

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

        OnMutated();

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
            OnMutated();

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
}
