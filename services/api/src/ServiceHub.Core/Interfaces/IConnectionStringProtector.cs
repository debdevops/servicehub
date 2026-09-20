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

    /// <summary>
    /// Returns a non-reversible fingerprint of the currently active encryption key — never the
    /// key material itself. Used to let an operator verify, e.g. before restoring a backup, that
    /// two environments share the same encryption key without ever exposing it.
    /// </summary>
    /// <returns>A short, stable identifier derived from the key (e.g. "sha256:ab12cd34...").</returns>
    string GetKeyFingerprint();
}
