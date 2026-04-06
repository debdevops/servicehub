using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Repository interface for namespace persistence operations.
/// </summary>
public interface INamespaceRepository
{
    /// <summary>
    /// Gets a namespace by its identifier.
    /// </summary>
    /// <param name="id">The namespace identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the namespace or a not found error.</returns>
    Task<Result<Namespace>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a namespace by its name.
    /// </summary>
    /// <param name="name">The namespace name.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the namespace or a not found error.</returns>
    Task<Result<Namespace>> GetByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all namespaces.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing all namespaces.</returns>
    Task<Result<IReadOnlyList<Namespace>>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all namespaces belonging to a specific owner (tenant).
    /// Use this in controller-layer calls to enforce per-caller isolation.
    /// Background workers that need to monitor all namespaces regardless of owner
    /// should use <see cref="GetAllAsync"/> instead.
    /// </summary>
    /// <param name="ownerId">The owner identifier derived from the caller's credential.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the owner's namespaces.</returns>
    Task<Result<IReadOnlyList<Namespace>>> GetByOwnerAsync(string ownerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all active namespaces.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing all active namespaces.</returns>
    Task<Result<IReadOnlyList<Namespace>>> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new namespace.
    /// </summary>
    /// <param name="namespace">The namespace to add.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> AddAsync(Namespace @namespace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing namespace.
    /// </summary>
    /// <param name="namespace">The namespace to update.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> UpdateAsync(Namespace @namespace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a namespace by its identifier.
    /// </summary>
    /// <param name="id">The namespace identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a namespace with the given name exists for a specific owner.
    /// </summary>
    /// <param name="name">The namespace name.</param>
    /// <param name="ownerId">The owner identifier to scope the check to.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if exists for the given owner; otherwise, false.</returns>
    Task<bool> ExistsAsync(string name, string ownerId, CancellationToken cancellationToken = default);
}
