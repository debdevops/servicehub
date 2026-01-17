using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Security;

/// <summary>
/// In-memory secrets manager implementation.
/// This is a placeholder for MVP purposes.
/// Production systems should integrate with Azure Key Vault.
/// </summary>
public sealed class SecretsManager : ISecretsManager
{
    private readonly ConcurrentDictionary<string, string> _secrets = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger<SecretsManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecretsManager"/> class.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    public SecretsManager(ILogger<SecretsManager> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<Result<string>> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return Task.FromResult(Result.Failure<string>(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "Secret name is required.")));
        }

        if (_secrets.TryGetValue(secretName, out var secretValue))
        {
            _logger.LogDebug("Retrieved secret {SecretName} from in-memory store", secretName);
            return Task.FromResult(Result.Success(secretValue));
        }

        _logger.LogDebug("Secret {SecretName} not found in in-memory store", secretName);
        return Task.FromResult(Result.Failure<string>(Error.NotFound(
            ErrorCodes.General.UnexpectedError,
            $"Secret '{secretName}' was not found.")));
    }

    /// <inheritdoc/>
    public Task<Result> SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "Secret name is required.")));
        }

        if (secretValue is null)
        {
            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "Secret value cannot be null.")));
        }

        _secrets[secretName] = secretValue;
        _logger.LogInformation("Set secret {SecretName} in in-memory store", secretName);

        return Task.FromResult(Result.Success());
    }

    /// <inheritdoc/>
    public Task<Result> DeleteSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "Secret name is required.")));
        }

        if (_secrets.TryRemove(secretName, out _))
        {
            _logger.LogInformation("Deleted secret {SecretName} from in-memory store", secretName);
            return Task.FromResult(Result.Success());
        }

        _logger.LogDebug("Secret {SecretName} not found for deletion", secretName);
        return Task.FromResult(Result.Failure(Error.NotFound(
            ErrorCodes.General.UnexpectedError,
            $"Secret '{secretName}' was not found.")));
    }

    /// <inheritdoc/>
    public Task<bool> ExistsAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return Task.FromResult(false);
        }

        return Task.FromResult(_secrets.ContainsKey(secretName));
    }
}
