using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Configuration;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.ServiceBus;

/// <summary>
/// Factory for creating and validating Azure Service Bus client instances.
/// Supports connection string, Entra ID (service principal/managed identity),
/// DefaultAzureCredential, and user-delegated OAuth authentication flows.
/// </summary>
public sealed class ServiceBusClientFactory : IServiceBusClientFactory
{
    private readonly IServiceBusClientCache _clientCache;
    private readonly IOptions<EntraIdOptions> _entraIdOptions;
    private readonly IOAuthService _oauthService;
    private readonly ILogger<ServiceBusClientFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusClientFactory"/> class.
    /// </summary>
    /// <param name="clientCache">The client cache for storing created clients.</param>
    /// <param name="entraIdOptions">The Entra ID configuration options.</param>
    /// <param name="oauthService">The OAuth service for user-delegated credentials.</param>
    /// <param name="logger">The logger instance.</param>
    public ServiceBusClientFactory(
        IServiceBusClientCache clientCache,
        IOptions<EntraIdOptions> entraIdOptions,
        IOAuthService oauthService,
        ILogger<ServiceBusClientFactory> logger)
    {
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _entraIdOptions = entraIdOptions ?? throw new ArgumentNullException(nameof(entraIdOptions));
        _oauthService = oauthService ?? throw new ArgumentNullException(nameof(oauthService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<Result> CreateClientAsync(Namespace @namespace, CancellationToken cancellationToken = default)
    {
        if (@namespace is null)
        {
            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.NotFound,
                "Namespace cannot be null.")));
        }

        return @namespace.AuthType switch
        {
            ConnectionAuthType.ConnectionString       => CreateConnectionStringClientAsync(@namespace),
            ConnectionAuthType.ManagedIdentity        => CreateEntraIdClientAsync(@namespace, cancellationToken),
            ConnectionAuthType.ServicePrincipal       => CreateEntraIdClientAsync(@namespace, cancellationToken),
            ConnectionAuthType.DefaultAzureCredential => CreateDefaultCredentialClientAsync(@namespace),
            ConnectionAuthType.UserDelegated          => CreateUserDelegatedClientAsync(@namespace),
            _ => Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                $"Unsupported authentication type: {@namespace.AuthType}")))
        };
    }

    /// <summary>
    /// Creates a client using SAS connection string authentication.
    /// </summary>
    private Task<Result> CreateConnectionStringClientAsync(Namespace @namespace)
    {
        if (string.IsNullOrWhiteSpace(@namespace.ConnectionString))
        {
            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Connection string is required for ConnectionString authentication.")));
        }

        var validationResult = ValidateConnectionString(@namespace.ConnectionString);
        if (validationResult.IsFailure)
        {
            return Task.FromResult(validationResult);
        }

        try
        {
            _clientCache.GetOrCreate(@namespace.Id, @namespace.ConnectionString);

            _logger.LogInformation(
                "Service Bus client created/retrieved for namespace {NamespaceId} ({NamespaceName})",
                @namespace.Id,
                @namespace.Name);

            return Task.FromResult(Result.Success());
        }
        catch (ServiceBusException ex) when (ex.Reason == ServiceBusFailureReason.ServiceCommunicationProblem)
        {
            _logger.LogError(ex,
                "Failed to communicate with Service Bus for namespace {NamespaceId}",
                @namespace.Id);

            return Task.FromResult(Result.Failure(Error.ExternalService(
                ErrorCodes.Namespace.ConnectionFailed,
                "Failed to communicate with Azure Service Bus. Please check your network connection.")));
        }
        catch (FormatException ex)
        {
            _logger.LogError(ex,
                "Invalid connection string format for namespace {NamespaceId}",
                @namespace.Id);

            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "The connection string format is invalid.")));
        }
        catch (ArgumentException ex)
        {
            _logger.LogError(ex,
                "Invalid argument when creating client for namespace {NamespaceId}",
                @namespace.Id);

            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                $"Invalid connection string: {ex.Message}")));
        }
    }

    /// <summary>
    /// Creates a client using Entra ID (ClientSecretCredential) authentication.
    /// ServiceHub authenticates as its own App Registration, which must be granted
    /// "Azure Service Bus Data Receiver" on the user's namespace.
    /// </summary>
    private Task<Result> CreateEntraIdClientAsync(
        Namespace @namespace,
        CancellationToken cancellationToken)
    {
        var options = _entraIdOptions.Value;

        if (!options.IsConfigured)
        {
            _logger.LogWarning(
                "Entra ID authentication requested for namespace {NamespaceId} but EntraId is not configured. " +
                "Set EntraId:ClientId, EntraId:ClientSecret, and EntraId:TenantId in configuration.",
                @namespace.Id);

            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Azure Entra ID authentication is not configured on this ServiceHub instance. " +
                "Contact your administrator or use connection string authentication.")));
        }

        var fqns = @namespace.Name.Contains('.')
            ? @namespace.Name
            : $"{@namespace.Name}.servicebus.windows.net";

        try
        {
            // ClientSecretCredential: ServiceHub authenticates as its App Registration,
            // which must have been granted "Azure Service Bus Data Receiver" on the user's namespace.
            var credential = new Azure.Identity.ClientSecretCredential(
                options.TenantId,
                options.ClientId,
                options.ClientSecret);

            _clientCache.GetOrCreate(@namespace.Id, credential, fqns);

            _logger.LogInformation(
                "Entra ID Service Bus client created for namespace {NamespaceId} ({Fqns})",
                @namespace.Id, fqns);

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create Entra ID Service Bus client for namespace {NamespaceId}",
                @namespace.Id);

            return Task.FromResult(Result.Failure(Error.ExternalService(
                ErrorCodes.Namespace.ConnectionFailed,
                "Failed to create Entra ID Service Bus client. Verify the namespace hostname and that ServiceHub has been granted the required role.")));
        }
    }

    /// <summary>
    /// Creates a client using DefaultAzureCredential (az login, managed identity, env vars).
    /// Useful for local development and Azure-hosted scenarios without explicit credentials.
    /// </summary>
    private Task<Result> CreateDefaultCredentialClientAsync(Namespace @namespace)
    {
        var fqns = @namespace.Name.Contains('.')
            ? @namespace.Name
            : $"{@namespace.Name}.servicebus.windows.net";

        try
        {
            var credential = new Azure.Identity.DefaultAzureCredential();
            _clientCache.GetOrCreate(@namespace.Id, credential, fqns);

            _logger.LogInformation(
                "DefaultAzureCredential Service Bus client created for namespace {NamespaceId}",
                @namespace.Id);

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create DefaultAzureCredential client for namespace {NamespaceId}",
                @namespace.Id);

            return Task.FromResult(Result.Failure(Error.ExternalService(
                ErrorCodes.Namespace.ConnectionFailed,
                "DefaultAzureCredential failed. Run 'az login' or ensure a managed identity is available.")));
        }
    }

    /// <summary>
    /// Creates a client using the user's own Azure OAuth delegated token.
    /// Retrieves the TokenCredential from the in-memory OAuth session referenced by the namespace.
    /// The user must be signed in via the Azure Entra ID tab in the Connect page.
    /// </summary>
    private Task<Result> CreateUserDelegatedClientAsync(Namespace @namespace)
    {
        if (string.IsNullOrEmpty(@namespace.OAuthSessionId))
        {
            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "User-delegated namespace is missing the OAuth session reference.")));
        }

        var credential = _oauthService.GetTokenCredential(@namespace.OAuthSessionId);
        if (credential is null)
        {
            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionFailed,
                "Your Azure sign-in session has expired. Please sign in again via the Azure Entra ID tab.")));
        }

        var fqns = @namespace.Name.Contains('.')
            ? @namespace.Name
            : $"{@namespace.Name}.servicebus.windows.net";

        try
        {
            _clientCache.GetOrCreate(@namespace.Id, credential, fqns);

            _logger.LogInformation(
                "User-delegated Service Bus client created for namespace {NamespaceId}",
                @namespace.Id);

            return Task.FromResult(Result.Success());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create user-delegated client for namespace {NamespaceId}", @namespace.Id);

            return Task.FromResult(Result.Failure(Error.ExternalService(
                ErrorCodes.Namespace.ConnectionFailed,
                "Failed to connect to Service Bus with your Azure identity. Ensure you have the 'Azure Service Bus Data Owner' role on the namespace.")));
        }
    }

    /// <inheritdoc/>
    public Result ValidateConnectionString(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Connection string is required."));
        }

        // Check for required components
        var hasEndpoint = connectionString.Contains("Endpoint=", StringComparison.OrdinalIgnoreCase);
        var hasSharedAccessKey = connectionString.Contains("SharedAccessKey=", StringComparison.OrdinalIgnoreCase);
        var hasSharedAccessSignature = connectionString.Contains("SharedAccessSignature=", StringComparison.OrdinalIgnoreCase);
        var hasSharedAccessKeyName = connectionString.Contains("SharedAccessKeyName=", StringComparison.OrdinalIgnoreCase);

        if (!hasEndpoint)
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Connection string must contain an 'Endpoint' component."));
        }

        if (!hasSharedAccessKey && !hasSharedAccessSignature)
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Connection string must contain either 'SharedAccessKey' or 'SharedAccessSignature'."));
        }

        if (hasSharedAccessKey && !hasSharedAccessKeyName)
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Connection string with 'SharedAccessKey' must also contain 'SharedAccessKeyName'."));
        }

        // SECURITY: Reject RootManageSharedAccessKey - use dedicated policies
        if (connectionString.Contains("RootManageSharedAccessKey", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Connection strings using 'RootManageSharedAccessKey' are not allowed. " +
                "Please create a dedicated Shared Access Policy with 'Manage', 'Send', and 'Listen' permissions. " +
                "Using root keys is a security risk and should be avoided."));
        }

        // Validate endpoint format
        try
        {
            var endpointStart = connectionString.IndexOf("Endpoint=", StringComparison.OrdinalIgnoreCase) + 9;
            var endpointEnd = connectionString.IndexOf(';', endpointStart);
            var endpoint = endpointEnd > endpointStart
                ? connectionString[endpointStart..endpointEnd]
                : connectionString[endpointStart..];

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri))
            {
                return Result.Failure(Error.Validation(
                    ErrorCodes.Namespace.EndpointInvalid,
                    "The Endpoint in the connection string is not a valid URI."));
            }

            if (uri.Scheme != "sb")
            {
                return Result.Failure(Error.Validation(
                    ErrorCodes.Namespace.EndpointInvalid,
                    "The Endpoint scheme must be 'sb://'."));
            }
        }
        catch
        {
            return Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                "Failed to parse the connection string endpoint."));
        }

        return Result.Success();
    }
}
