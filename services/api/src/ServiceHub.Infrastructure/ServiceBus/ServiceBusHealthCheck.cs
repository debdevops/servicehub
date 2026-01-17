using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.ServiceBus;

/// <summary>
/// Health check implementation for Azure Service Bus connectivity.
/// Validates that all configured namespaces are accessible.
/// </summary>
public sealed class ServiceBusHealthCheck : IHealthCheck
{
    private readonly IServiceBusClientCache _clientCache;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<ServiceBusHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusHealthCheck"/> class.
    /// </summary>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="logger">The logger instance.</param>
    public ServiceBusHealthCheck(
        IServiceBusClientCache clientCache,
        INamespaceRepository namespaceRepository,
        ILogger<ServiceBusHealthCheck> logger)
    {
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();

        try
        {
            var namespacesResult = await _namespaceRepository.GetActiveAsync(cancellationToken).ConfigureAwait(false);

            if (namespacesResult.IsFailure)
            {
                return HealthCheckResult.Degraded(
                    "Failed to retrieve active namespaces.",
                    data: data);
            }

            var namespaces = namespacesResult.Value;
            data["TotalNamespaces"] = namespaces.Count;

            if (namespaces.Count == 0)
            {
                return HealthCheckResult.Healthy(
                    "No active namespaces configured.",
                    data: data);
            }

            var healthyCount = 0;
            var unhealthyNamespaces = new List<string>();

            foreach (var ns in namespaces)
            {
                var result = await CheckNamespaceHealthAsync(ns.Id, ns.ConnectionString, cancellationToken)
                    .ConfigureAwait(false);

                if (result.IsSuccess && result.Value)
                {
                    healthyCount++;
                }
                else
                {
                    unhealthyNamespaces.Add(ns.Name);
                }
            }

            data["HealthyNamespaces"] = healthyCount;
            data["UnhealthyNamespaces"] = unhealthyNamespaces.Count;

            if (unhealthyNamespaces.Count > 0)
            {
                data["UnhealthyNamespaceNames"] = string.Join(", ", unhealthyNamespaces);
            }

            if (unhealthyNamespaces.Count == namespaces.Count)
            {
                return HealthCheckResult.Unhealthy(
                    "All Service Bus namespaces are unhealthy.",
                    data: data);
            }

            if (unhealthyNamespaces.Count > 0)
            {
                return HealthCheckResult.Degraded(
                    $"{unhealthyNamespaces.Count} of {namespaces.Count} namespaces are unhealthy.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"All {namespaces.Count} Service Bus namespaces are healthy.",
                data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during Service Bus health check");

            return HealthCheckResult.Unhealthy(
                "Service Bus health check failed with an exception.",
                ex,
                data);
        }
    }

    /// <summary>
    /// Checks the health of a specific namespace.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result indicating whether the namespace is healthy.</returns>
    public async Task<Result<bool>> CheckNamespaceHealthAsync(
        Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        var namespaceResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken)
            .ConfigureAwait(false);

        if (namespaceResult.IsFailure)
        {
            return Result.Failure<bool>(namespaceResult.Error);
        }

        return await CheckNamespaceHealthAsync(
            namespaceId,
            namespaceResult.Value.ConnectionString,
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Result<bool>> CheckNamespaceHealthAsync(
        Guid namespaceId,
        string? connectionString,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return Result.Failure<bool>(Error.Validation(
                ErrorCodes.Namespace.ConnectionStringRequired,
                "Connection string is not configured."));
        }

        try
        {
            var clientWrapper = _clientCache.GetOrCreate(namespaceId, connectionString);
            var result = await clientWrapper.TestConnectionAsync(cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                _logger.LogDebug("Health check passed for namespace {NamespaceId}", namespaceId);
            }
            else
            {
                _logger.LogWarning(
                    "Health check failed for namespace {NamespaceId}: {Error}",
                    namespaceId,
                    result.Error.Message);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Exception during health check for namespace {NamespaceId}", namespaceId);

            return Result.Failure<bool>(Error.ExternalService(
                ErrorCodes.Namespace.ConnectionFailed,
                $"Health check failed: {ex.Message}"));
        }
    }
}
