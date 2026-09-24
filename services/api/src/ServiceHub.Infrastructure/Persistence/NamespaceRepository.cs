using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// SQLite-backed <see cref="INamespaceRepository"/>. Every namespace has exactly one owner and is
/// visible only to that owner (plus callers that read across owners on purpose, such as the
/// background agents, which use <see cref="GetAllAsync"/>).
/// </summary>
/// <remarks>
/// Reads are untracked. The connection string comes back as the stored <c>ENC[…]</c> envelope —
/// decrypting it is the job of whoever builds a provider client, never of the repository — and
/// <see cref="ConnectionStringEncryptionInterceptor"/> protects it on the way in.
/// </remarks>
public sealed class NamespaceRepository : INamespaceRepository
{
    private readonly ServiceHubDbContext _dbContext;
    private readonly ILogger<NamespaceRepository> _logger;

    /// <summary>Creates the repository over the request-scoped database context.</summary>
    public NamespaceRepository(ServiceHubDbContext dbContext, ILogger<NamespaceRepository> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
        return ns is null
            ? Result.Failure<Namespace>(Error.NotFound(
                ErrorCodes.Namespace.NotFound, $"Namespace with ID '{id}' was not found."))
            : Result.Success(ns);
    }

    /// <inheritdoc/>
    public async Task<Result<Namespace>> GetByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return Result.Failure<Namespace>(Error.Validation(
                ErrorCodes.Namespace.NameRequired, "Namespace name is required."));
        }

        // Names are stored lower-invariant, so normalising the input gives case-insensitive
        // matching without a case-insensitive SQL collation.
        var normalizedName = name.Trim().ToLowerInvariant();
        var ns = await _dbContext.Namespaces.AsNoTracking().FirstOrDefaultAsync(n => n.Name == normalizedName, cancellationToken);
        return ns is null
            ? Result.Failure<Namespace>(Error.NotFound(
                ErrorCodes.Namespace.NotFound, $"Namespace with name '{name}' was not found."))
            : Result.Success(ns);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Namespace>>> GetAllAsync(CancellationToken cancellationToken = default) =>
        Result.Success<IReadOnlyList<Namespace>>(
            await _dbContext.Namespaces.AsNoTracking().ToListAsync(cancellationToken));

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Namespace>>> GetByOwnerAsync(
        string ownerId, IReadOnlySet<Guid>? allowedNamespaceIds = null, CancellationToken cancellationToken = default)
    {
        var owned = await _dbContext.Namespaces.AsNoTracking()
            .Where(n => n.OwnerId == ownerId)
            .ToListAsync(cancellationToken);

        IReadOnlyList<Namespace> visible = allowedNamespaceIds is null
            ? owned
            : owned.Where(n => allowedNamespaceIds.Contains(n.Id)).ToList();

        _logger.LogDebug("Retrieved {Count} namespaces for owner {OwnerId}", visible.Count, ownerId);
        return Result.Success(visible);
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Namespace>>> GetActiveAsync(CancellationToken cancellationToken = default) =>
        Result.Success<IReadOnlyList<Namespace>>(
            await _dbContext.Namespaces.AsNoTracking().Where(n => n.IsActive).ToListAsync(cancellationToken));

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

        // Reload the persisted row so the checks below hold whether the caller passed a tracked or
        // a fully detached instance.
        var existing = await _dbContext.Namespaces.AsNoTracking()
            .FirstOrDefaultAsync(n => n.Id == @namespace.Id, cancellationToken);
        if (existing is null)
        {
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
        await _dbContext.SaveChangesAsync(cancellationToken);
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
            return Result.Failure(Error.NotFound(
                ErrorCodes.Namespace.NotFound, $"Namespace with ID '{id}' was not found."));
        }

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
