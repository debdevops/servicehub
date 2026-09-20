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
    private const int MaxConcurrentChecks = 10;

    private readonly IServiceBusClientCache _clientCache;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly IConnectionStringProtector _connectionStringProtector;
    private readonly ILogger<ServiceBusHealthCheck> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServiceBusHealthCheck"/> class.
    /// </summary>
    /// <param name="clientCache">The Service Bus client cache.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="connectionStringProtector">The connection string protector used to decrypt stored connection strings.</param>
    /// <param name="logger">The logger instance.</param>
    public ServiceBusHealthCheck(
        IServiceBusClientCache clientCache,
        INamespaceRepository namespaceRepository,
        IConnectionStringProtector connectionStringProtector,
        ILogger<ServiceBusHealthCheck> logger)
    {
        _clientCache = clientCache ?? throw new ArgumentNullException(nameof(clientCache));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _connectionStringProtector = connectionStringProtector ?? throw new ArgumentNullException(nameof(connectionStringProtector));
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

            // AWS/GCP namespaces use provider-specific connectivity and must not be tested with
            // the Azure SDK client cache — doing so throws FormatException on non-Azure
            // connection strings.
            var azureNamespaces = namespaces.Where(ns => ns.Provider == ServiceHub.Core.Enums.CloudProviderType.Azure).ToList();
            var skippedCount = namespaces.Count - azureNamespaces.Count;

            // Checked with bounded concurrency, not sequentially: a fleet with several unreachable
            // namespaces (stale credentials, a deleted resource group) previously made this check's
            // total duration scale linearly with namespace count — one real dev fleet with 10 stale
            // registrations turned a sub-second check into a 60+ second one, which is bad for an
            // endpoint operators and orchestrators alike expect to answer quickly. Mirrors
            // DlqMonitorWorker's own bounded-parallel-scan pattern for the same class of problem.
            using var semaphore = new SemaphoreSlim(MaxConcurrentChecks);
            var checkTasks = azureNamespaces.Select(async ns =>
            {
                await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    var result = await CheckNamespaceHealthAsync(ns.Id, ns.ConnectionString, cancellationToken)
                        .ConfigureAwait(false);
                    return (ns.Name, IsHealthy: result.IsSuccess && result.Value);
                }
                finally
                {
                    semaphore.Release();
                }
            });

            var checkResults = await Task.WhenAll(checkTasks).ConfigureAwait(false);

            var healthyCount = checkResults.Count(r => r.IsHealthy);
            var unhealthyNamespaces = checkResults.Where(r => !r.IsHealthy).Select(r => r.Name).ToList();

            var azureNamespaceCount = azureNamespaces.Count;

            data["HealthyNamespaces"] = healthyCount;
            data["UnhealthyNamespaces"] = unhealthyNamespaces.Count;

            if (skippedCount > 0)
            {
                data["SkippedNamespaces"] = skippedCount;
            }

            // Namespace names are deliberately never exposed here: GetActiveAsync() returns every
            // owner's namespaces unscoped, and /health is unauthenticated, so a name would leak
            // one tenant's namespace naming to any caller who can merely reach the health endpoint.

            if (azureNamespaceCount == 0)
            {
                return HealthCheckResult.Healthy(
                    "No Azure Service Bus namespaces configured — health check skipped.",
                    data: data);
            }

            if (unhealthyNamespaces.Count == azureNamespaceCount)
            {
                return HealthCheckResult.Unhealthy(
                    "All Azure Service Bus namespaces are unhealthy.",
                    data: data);
            }

            if (unhealthyNamespaces.Count > 0)
            {
                return HealthCheckResult.Degraded(
                    $"{unhealthyNamespaces.Count} of {azureNamespaceCount} Azure Service Bus namespaces are unhealthy.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"All {azureNamespaceCount} Azure Service Bus namespace(s) are healthy.",
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

        var unprotectResult = _connectionStringProtector.Unprotect(connectionString);
        if (unprotectResult.IsFailure)
        {
            _logger.LogError(
                "Failed to decrypt connection string for namespace {NamespaceId}: {Error}",
                namespaceId,
                unprotectResult.Error.Message);

            return Result.Failure<bool>(unprotectResult.Error);
        }

        try
        {
            var clientWrapper = _clientCache.GetOrCreate(namespaceId, unprotectResult.Value);
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
