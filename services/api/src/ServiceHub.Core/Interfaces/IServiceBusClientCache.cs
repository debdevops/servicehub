namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Interface for caching Service Bus client instances to prevent socket exhaustion.
/// Implementations must be thread-safe.
/// </summary>
public interface IServiceBusClientCache : IAsyncDisposable
{
    /// <summary>
    /// Gets or creates a cached client wrapper for the specified namespace.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="connectionString">The connection string for the namespace.</param>
    /// <returns>A cached client wrapper.</returns>
    IServiceBusClientWrapper GetOrCreate(Guid namespaceId, string connectionString);

    /// <summary>
    /// Removes and disposes the client for the specified namespace.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task RemoveAsync(Guid namespaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a client exists for the specified namespace.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <returns>True if a client exists; otherwise, false.</returns>
    bool Contains(Guid namespaceId);
}
