using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Factory interface for creating Service Bus client instances.
/// Implementations handle connection establishment and validation.
/// </summary>
public interface IServiceBusClientFactory
{
    /// <summary>
    /// Creates a Service Bus client for the specified namespace.
    /// </summary>
    /// <param name="namespace">The namespace configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure of client creation.</returns>
    Task<Result> CreateClientAsync(Namespace @namespace, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the connection string format without establishing a connection.
    /// </summary>
    /// <param name="connectionString">The connection string to validate.</param>
    /// <returns>A result indicating whether the connection string is valid.</returns>
    Result ValidateConnectionString(string connectionString);
}
