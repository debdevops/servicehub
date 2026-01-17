using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Interface for managing secrets such as connection strings and API keys.
/// Future implementations will integrate with Azure Key Vault.
/// </summary>
public interface ISecretsManager
{
    /// <summary>
    /// Gets a secret by its name.
    /// </summary>
    /// <param name="secretName">The name of the secret.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing the secret value.</returns>
    Task<Result<string>> GetSecretAsync(string secretName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets a secret value.
    /// </summary>
    /// <param name="secretName">The name of the secret.</param>
    /// <param name="secretValue">The value of the secret.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a secret.
    /// </summary>
    /// <param name="secretName">The name of the secret to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating success or failure.</returns>
    Task<Result> DeleteSecretAsync(string secretName, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if a secret exists.
    /// </summary>
    /// <param name="secretName">The name of the secret.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the secret exists; otherwise, false.</returns>
    Task<bool> ExistsAsync(string secretName, CancellationToken cancellationToken = default);
}
