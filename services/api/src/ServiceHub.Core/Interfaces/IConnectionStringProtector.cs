using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Interface for protecting sensitive connection string data.
/// </summary>
public interface IConnectionStringProtector
{
    /// <summary>
    /// Protects a connection string for storage.
    /// </summary>
    /// <param name="connectionString">The plain text connection string.</param>
    /// <returns>A result containing the protected connection string.</returns>
    Result<string> Protect(string connectionString);

    /// <summary>
    /// Unprotects a previously protected connection string.
    /// </summary>
    /// <param name="protectedConnectionString">The protected connection string.</param>
    /// <returns>A result containing the plain text connection string.</returns>
    Result<string> Unprotect(string protectedConnectionString);

    /// <summary>
    /// Masks a connection string for display purposes.
    /// </summary>
    /// <param name="connectionString">The connection string to mask.</param>
    /// <returns>A masked version of the connection string safe for display.</returns>
    string Mask(string connectionString);
}
