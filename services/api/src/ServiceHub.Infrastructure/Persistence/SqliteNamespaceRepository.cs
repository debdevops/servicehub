using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed <see cref="INamespaceRepository"/> (M2 of the persistence wave) — replaces the
/// JSON-file-backed <c>InMemoryNamespaceRepository</c> in DI. <see cref="Namespace.SharedWithOwnerIds"/>
/// is not an EF-mapped column (see <c>DlqDbContext.ConfigureNamespace</c>'s <c>Ignore</c> call) —
/// every read here hydrates it from <see cref="DlqDbContext.NamespaceSharedOwners"/> afterward, using
/// the same reflection idiom <c>InMemoryNamespaceRepository.Rehydrate</c> already established for
/// this exact property (its private setter has no public bulk mutator).
/// </summary>
public sealed class SqliteNamespaceRepository : INamespaceRepository
{
    private readonly DlqDbContext _dbContext;
    private readonly ILogger<SqliteNamespaceRepository> _logger;

    public SqliteNamespaceRepository(DlqDbContext dbContext, ILogger<SqliteNamespaceRepository> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static readonly PropertyInfo SharedWithOwnerIdsProperty = typeof(Namespace).GetProperty(
        nameof(Namespace.SharedWithOwnerIds),
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;

    private static void SetSharedWithOwnerIds(Namespace target, IReadOnlyList<string> value) =>
        SharedWithOwnerIdsProperty.SetValue(target, value);

    /// <summary>Hydrates <see cref="Namespace.SharedWithOwnerIds"/> on every namespace in
    /// <paramref name="namespaces"/> from a single batched query, rather than one query per row.</summary>
    private async Task HydrateSharedOwnersAsync(IReadOnlyList<Namespace> namespaces, CancellationToken cancellationToken)
    {
        if (namespaces.Count == 0)
        {
            return;
        }

        var ids = namespaces.Select(n => n.Id).ToList();
        var shares = await _dbContext.NamespaceSharedOwners
            .AsNoTracking()
            .Where(s => ids.Contains(s.NamespaceId))
            .ToListAsync(cancellationToken);

        var byNamespace = shares
            .GroupBy(s => s.NamespaceId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<string>)g.Select(s => s.OwnerId).ToArray());

        foreach (var ns in namespaces)
        {
            SetSharedWithOwnerIds(ns, byNamespace.TryGetValue(ns.Id, out var owners) ? owners : []);
        }
    }

    /// <inheritdoc/>
    public async Task<Result<Namespace>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure<Namespace>(Error.Validation(
                ErrorCodes.Namespace.NotFound, "Namespace ID cannot be empty."));
        }

        var ns = await _dbContext.Namespaces.AsNoTracking().FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (ns is null)
        {
            return Result.Failure<Namespace>(Error.NotFound(
                ErrorCodes.Namespace.NotFound, $"Namespace with ID '{id}' was not found."));
        }

        await HydrateSharedOwnersAsync([ns], cancellationToken);
        return Result.Success(ns);
    }

    /// <inheritdoc/>
    public async Task<Result<Namespace>> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Namespace>(Error.Validation(
                ErrorCodes.Namespace.NameRequired, "Namespace name is required."));
        }

        // Namespace.Create/CreateWithManagedIdentity always store Name lower-invariant, so
        // normalizing the query input this way reproduces OrdinalIgnoreCase semantics without
        // needing a case-insensitive SQL collation.
        var normalizedName = name.Trim().ToLowerInvariant();
        var ns = await _dbContext.Namespaces.AsNoTracking().FirstOrDefaultAsync(n => n.Name == normalizedName, cancellationToken);
        if (ns is null)
        {
            return Result.Failure<Namespace>(Error.NotFound(
                ErrorCodes.Namespace.NotFound, $"Namespace with name '{name}' was not found."));
        }

        await HydrateSharedOwnersAsync([ns], cancellationToken);
        return Result.Success(ns);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Namespace>>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var namespaces = await _dbContext.Namespaces.AsNoTracking().ToListAsync(cancellationToken);
        await HydrateSharedOwnersAsync(namespaces, cancellationToken);
        return Result.Success<IReadOnlyList<Namespace>>(namespaces);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Namespace>>> GetByOwnerAsync(
        string ownerId, IReadOnlySet<Guid>? allowedNamespaceIds = null, CancellationToken cancellationToken = default)
    {
        // Namespace.IsAccessibleBy needs SharedWithOwnerIds hydrated to evaluate correctly, so this
        // loads every namespace and filters in-memory — self-hosted namespace counts are small
        // (tens, not millions), matching the in-memory repository's own pattern exactly.
        var all = await _dbContext.Namespaces.AsNoTracking().ToListAsync(cancellationToken);
        await HydrateSharedOwnersAsync(all, cancellationToken);

        var filtered = all.Where(n => n.IsAccessibleBy(ownerId, allowedNamespaceIds)).ToList();

        _logger.LogDebug("Retrieved {Count} namespaces for owner {OwnerId}", filtered.Count, ownerId);
        return Result.Success<IReadOnlyList<Namespace>>(filtered);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Namespace>>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var namespaces = await _dbContext.Namespaces.AsNoTracking().Where(n => n.IsActive).ToListAsync(cancellationToken);
        await HydrateSharedOwnersAsync(namespaces, cancellationToken);
        return Result.Success<IReadOnlyList<Namespace>>(namespaces);
    }

    /// <inheritdoc/>
    public async Task<Result> AddAsync(Namespace @namespace, CancellationToken cancellationToken = default)
    {
        if (@namespace is null)
        {
            return Result.Failure(Error.Validation(ErrorCodes.Namespace.NotFound, "Namespace cannot be null."));
        }

        var duplicateName = await _dbContext.Namespaces.AsNoTracking()
            .AnyAsync(n => n.Name == @namespace.Name && n.OwnerId == @namespace.OwnerId, cancellationToken);
        if (duplicateName)
        {
            _logger.LogWarning(
                "Attempted to add namespace with duplicate name {NamespaceName}",
                LogRedactor.SanitiseForLog(@namespace.Name));

            return Result.Failure(Error.Conflict(
                ErrorCodes.Namespace.AlreadyExists, $"A namespace with the name '{@namespace.Name}' already exists."));
        }

        _dbContext.Namespaces.Add(@namespace);

        if (@namespace.SharedWithOwnerIds.Count > 0)
        {
            _dbContext.NamespaceSharedOwners.AddRange(
                @namespace.SharedWithOwnerIds.Select(o => new NamespaceSharedOwner { NamespaceId = @namespace.Id, OwnerId = o }));
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return Result.Failure(Error.Conflict(
                ErrorCodes.Namespace.AlreadyExists, $"A namespace with the ID '{@namespace.Id}' already exists."));
        }

        _logger.LogInformation(
            "Added namespace {NamespaceId} ({NamespaceName})", @namespace.Id, LogRedactor.SanitiseForLog(@namespace.Name));
        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result> UpdateAsync(Namespace @namespace, CancellationToken cancellationToken = default)
    {
        if (@namespace is null)
        {
            return Result.Failure(Error.Validation(ErrorCodes.Namespace.NotFound, "Namespace cannot be null."));
        }

        // Always reload the persisted state fresh (rather than trusting change-tracker identity),
        // so this method behaves correctly whether @namespace came from this same DbContext scope
        // or is a fully detached instance — mirrors the in-memory repository's own dictionary-read
        // validation exactly.
        var existing = await _dbContext.Namespaces.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == @namespace.Id, cancellationToken);
        if (existing is null)
        {
            _logger.LogWarning("Attempted to update non-existent namespace {NamespaceId}", @namespace.Id);
            return Result.Failure(Error.NotFound(
                ErrorCodes.Namespace.NotFound, $"Namespace with ID '{@namespace.Id}' was not found."));
        }

        if (!string.Equals(existing.OwnerId, @namespace.OwnerId, StringComparison.Ordinal))
        {
            _logger.LogWarning("Attempted to change OwnerId on namespace {NamespaceId}", @namespace.Id);
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.NotFound, "Cannot modify the owner of a namespace."));
        }

        var duplicateName = await _dbContext.Namespaces.AsNoTracking()
            .AnyAsync(n => n.Id != @namespace.Id && n.Name == @namespace.Name && n.OwnerId == @namespace.OwnerId, cancellationToken);
        if (duplicateName)
        {
            return Result.Failure(Error.Conflict(
                ErrorCodes.Namespace.AlreadyExists, $"A namespace with the name '{@namespace.Name}' already exists."));
        }

        _dbContext.Namespaces.Update(@namespace);

        // Reconcile the NamespaceSharedOwners join rows to match @namespace.SharedWithOwnerIds
        // exactly — add what's missing, remove what's no longer present.
        var currentShares = await _dbContext.NamespaceSharedOwners
            .Where(s => s.NamespaceId == @namespace.Id)
            .ToListAsync(cancellationToken);
        var desired = @namespace.SharedWithOwnerIds.ToHashSet(StringComparer.Ordinal);
        var current = currentShares.Select(s => s.OwnerId).ToHashSet(StringComparer.Ordinal);

        var toRemove = currentShares.Where(s => !desired.Contains(s.OwnerId)).ToList();
        if (toRemove.Count > 0)
        {
            _dbContext.NamespaceSharedOwners.RemoveRange(toRemove);
        }

        var toAdd = desired.Where(o => !current.Contains(o))
            .Select(o => new NamespaceSharedOwner { NamespaceId = @namespace.Id, OwnerId = o })
            .ToList();
        if (toAdd.Count > 0)
        {
            _dbContext.NamespaceSharedOwners.AddRange(toAdd);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated namespace {NamespaceId} ({NamespaceName})", @namespace.Id, LogRedactor.SanitiseForLog(@namespace.Name));
        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (id == Guid.Empty)
        {
            return Result.Failure(Error.Validation(ErrorCodes.Namespace.NotFound, "Namespace ID cannot be empty."));
        }

        var ns = await _dbContext.Namespaces.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (ns is null)
        {
            _logger.LogWarning("Attempted to delete non-existent namespace {NamespaceId}", id);
            return Result.Failure(Error.NotFound(
                ErrorCodes.Namespace.NotFound, $"Namespace with ID '{id}' was not found."));
        }

        // NamespaceSharedOwners rows cascade-delete at the SQLite engine level (FK configured with
        // ON DELETE CASCADE, and Microsoft.Data.Sqlite enables foreign_keys enforcement by default)
        // — no need to load/remove them explicitly here.
        _dbContext.Namespaces.Remove(ns);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Deleted namespace {NamespaceId} ({NamespaceName})", id, LogRedactor.SanitiseForLog(ns.Name));
        return Result.Success();
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string name, string ownerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var normalizedName = name.Trim().ToLowerInvariant();
        return await _dbContext.Namespaces.AsNoTracking()
            .AnyAsync(n => n.Name == normalizedName && n.OwnerId == ownerId, cancellationToken);
    }
}
