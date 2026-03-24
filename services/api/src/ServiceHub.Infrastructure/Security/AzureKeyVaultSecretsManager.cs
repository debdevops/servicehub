using Azure;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Security;

/// <summary>
/// Azure Key Vault implementation of <see cref="ISecretsManager"/>.
/// Used in production environments to securely manage secrets.
/// </summary>
public sealed class AzureKeyVaultSecretsManager : ISecretsManager
{
    private readonly SecretClient _client;
    private readonly ILogger<AzureKeyVaultSecretsManager> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="AzureKeyVaultSecretsManager"/> class.
    /// </summary>
    /// <param name="client">The Azure Key Vault secret client.</param>
    /// <param name="logger">The logger instance.</param>
    public AzureKeyVaultSecretsManager(SecretClient client, ILogger<AzureKeyVaultSecretsManager> logger)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result<string>> GetSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return Result.Failure<string>(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "Secret name is required."));
        }

        try
        {
            var response = await _client.GetSecretAsync(NormalizeSecretName(secretName), cancellationToken: cancellationToken);
            _logger.LogDebug("Retrieved secret {SecretName} from Azure Key Vault", secretName);
            return Result.Success(response.Value.Value);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Secret {SecretName} not found in Azure Key Vault", secretName);
            return Result.Failure<string>(Error.NotFound(
                ErrorCodes.General.UnexpectedError,
                $"Secret '{secretName}' was not found."));
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to retrieve secret {SecretName} from Azure Key Vault", secretName);
            return Result.Failure<string>(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                $"Failed to retrieve secret: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> SetSecretAsync(string secretName, string secretValue, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "Secret name is required."));
        }

        if (secretValue is null)
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "Secret value cannot be null."));
        }

        try
        {
            await _client.SetSecretAsync(NormalizeSecretName(secretName), secretValue, cancellationToken);
            _logger.LogInformation("Set secret {SecretName} in Azure Key Vault", secretName);
            return Result.Success();
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to set secret {SecretName} in Azure Key Vault", secretName);
            return Result.Failure(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                $"Failed to set secret: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> DeleteSecretAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.General.InvalidRequest,
                "Secret name is required."));
        }

        try
        {
            var operation = await _client.StartDeleteSecretAsync(NormalizeSecretName(secretName), cancellationToken);
            await operation.WaitForCompletionAsync(cancellationToken);
            _logger.LogInformation("Deleted secret {SecretName} from Azure Key Vault", secretName);
            return Result.Success();
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogDebug("Secret {SecretName} not found for deletion in Azure Key Vault", secretName);
            return Result.Failure(Error.NotFound(
                ErrorCodes.General.UnexpectedError,
                $"Secret '{secretName}' was not found."));
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Failed to delete secret {SecretName} from Azure Key Vault", secretName);
            return Result.Failure(Error.Internal(
                ErrorCodes.General.UnexpectedError,
                $"Failed to delete secret: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<bool> ExistsAsync(string secretName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(secretName))
        {
            return false;
        }

        try
        {
            await _client.GetSecretAsync(NormalizeSecretName(secretName), cancellationToken: cancellationToken);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
        catch (RequestFailedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Azure Key Vault secret names allow only alphanumeric characters and hyphens.
    /// This normalizes dots, underscores, and colons to hyphens.
    /// </summary>
    private static string NormalizeSecretName(string secretName)
    {
        return secretName
            .Replace('.', '-')
            .Replace('_', '-')
            .Replace(':', '-');
    }
}
