using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Aws;

/// <summary>
/// Health check implementation for AWS SQS connectivity.
/// Validates that at least one SQS queue is reachable for each configured AWS namespace.
/// Returns <see cref="HealthCheckResult.Healthy"/> with an informational message when
/// no AWS namespaces are configured, so the overall health endpoint is never degraded
/// solely because a user has not yet added an AWS connection.
/// </summary>
public sealed class AwsHealthCheck : IHealthCheck
{
    private readonly IAwsClientFactory _clientFactory;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<AwsHealthCheck> _logger;

    /// <summary>Timeout for each per-namespace SQS list call.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="AwsHealthCheck"/> class.
    /// </summary>
    /// <param name="clientFactory">The AWS client factory.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="logger">The logger instance.</param>
    public AwsHealthCheck(
        IAwsClientFactory clientFactory,
        INamespaceRepository namespaceRepository,
        ILogger<AwsHealthCheck> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
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
            var namespacesResult = await _namespaceRepository.GetActiveAsync(cancellationToken)
                .ConfigureAwait(false);

            if (namespacesResult.IsFailure)
            {
                return HealthCheckResult.Degraded(
                    "Failed to retrieve active namespaces.",
                    data: data);
            }

            var awsNamespaces = namespacesResult.Value
                .Where(ns => ns.Provider == CloudProviderType.Aws)
                .ToList();

            data["AwsNamespaces"] = awsNamespaces.Count;

            if (awsNamespaces.Count == 0)
            {
                return HealthCheckResult.Healthy("No AWS namespaces configured.", data: data);
            }

            var healthyCount = 0;
            var unhealthyNames = new List<string>();

            foreach (var ns in awsNamespaces)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(ProbeTimeout);

                    var sqsClient = _clientFactory.GetSqsClient(ns);
                    await sqsClient.ListQueuesAsync(
                        new ListQueuesRequest { MaxResults = 1 },
                        cts.Token).ConfigureAwait(false);

                    healthyCount++;
                    _logger.LogDebug("AWS health check passed for namespace {NamespaceId}", ns.Id);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    unhealthyNames.Add(ns.Name);
                    _logger.LogWarning(ex, "AWS health check failed for namespace {NamespaceId}", ns.Id);
                }
            }

            data["HealthyAwsNamespaces"] = healthyCount;
            data["UnhealthyAwsNamespaces"] = unhealthyNames.Count;

            if (unhealthyNames.Count > 0)
                data["UnhealthyAwsNamespaceNames"] = string.Join(", ", unhealthyNames);

            if (unhealthyNames.Count == awsNamespaces.Count)
            {
                return HealthCheckResult.Unhealthy(
                    "All AWS SQS namespaces are unreachable.",
                    data: data);
            }

            if (unhealthyNames.Count > 0)
            {
                return HealthCheckResult.Degraded(
                    $"{unhealthyNames.Count} of {awsNamespaces.Count} AWS namespaces are unreachable.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"All {awsNamespaces.Count} AWS SQS namespaces are reachable.",
                data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during AWS SQS health check");
            return HealthCheckResult.Unhealthy(
                "AWS SQS health check failed with an exception.",
                ex,
                data);
        }
    }
}
