using Azure.Messaging.ServiceBus;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.ServiceBus;

/// <summary>
/// Factory for creating and validating Azure Service Bus client instances.
/// </summary>
public sealed class ServiceBusClientFactory : IServiceBusClientFactory
{
    private readonly IServiceBusClientCache _clientCache;
    private readonly ILogger<ServiceBusClientFactory> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusClientFactory"/> class.
    /// </summary>
    /// <param name="clientCache">The client cache for storing created clients.</param>
    /// <param name="logger">The logger instance.</param>
    public ServiceBusClientFactory(
        IServiceBusClientCache clientCache,
        ILogger<ServiceBusClientFactory> logger)
    {
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
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

        if (@namespace.AuthType != ConnectionAuthType.ConnectionString)
        {
            _logger.LogWarning(
                "Attempted to create client for namespace {NamespaceId} with unsupported auth type {AuthType}",
                @namespace.Id,
                @namespace.AuthType);

            return Task.FromResult(Result.Failure(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringInvalid,
                $"Authentication type '{@namespace.AuthType}' is not yet supported. Use ConnectionString authentication.")));
        }

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
            // Get or create the cached client - this validates the connection string format
            var clientWrapper = _clientCache.GetOrCreate(@namespace.Id, @namespace.ConnectionString);

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
