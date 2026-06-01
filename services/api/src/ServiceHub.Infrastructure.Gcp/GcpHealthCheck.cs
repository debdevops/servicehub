using Google.Cloud.PubSub.V1;
using Grpc.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Gcp;

/// <summary>
/// Health check implementation for GCP Pub/Sub connectivity.
/// Validates that the Pub/Sub subscriber API is reachable for each configured GCP namespace
/// by listing up to one subscription in the project.
/// Returns <see cref="HealthCheckResult.Healthy"/> when no GCP namespaces are configured.
/// </summary>
public sealed class GcpHealthCheck : IHealthCheck
{
    private readonly IGcpClientFactory _clientFactory;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<GcpHealthCheck> _logger;

    /// <summary>Timeout for each per-namespace Pub/Sub probe call.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Initializes a new instance of the <see cref="GcpHealthCheck"/> class.
    /// </summary>
    /// <param name="clientFactory">The GCP client factory.</param>
    /// <param name="namespaceRepository">The namespace repository.</param>
    /// <param name="logger">The logger instance.</param>
    public GcpHealthCheck(
        IGcpClientFactory clientFactory,
        INamespaceRepository namespaceRepository,
        ILogger<GcpHealthCheck> logger)
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

            var gcpNamespaces = namespacesResult.Value
                .Where(ns => ns.Provider == CloudProviderType.Gcp)
                .ToList();

            data["GcpNamespaces"] = gcpNamespaces.Count;

            if (gcpNamespaces.Count == 0)
            {
                return HealthCheckResult.Healthy("No GCP namespaces configured.", data: data);
            }

            var healthyCount = 0;
            var unhealthyNames = new List<string>();

            foreach (var ns in gcpNamespaces)
            {
                try
                {
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    cts.CancelAfter(ProbeTimeout);

                    // Use a well-known health-probe topic name; any 404-style RpcException means
                    // connectivity works (the project is reachable) — other gRPC errors mean unreachable.
                    var client = await _clientFactory.GetSubscriberClientAsync(
                        ns, "_servicehub_health_probe_", cts.Token).ConfigureAwait(false);

                    // Attempt a cheap list call (page size 1) to validate the connection.
                    if (string.IsNullOrWhiteSpace(ns.GcpProjectId))
                        throw new InvalidOperationException($"GcpProjectId is required for health check on namespace {ns.Id}");

                    var request = new ListSubscriptionsRequest { Project = $"projects/{ns.GcpProjectId}" };
                    var asyncEnumerable = client.ListSubscriptionsAsync(request);
                    var enumerator = asyncEnumerable.GetAsyncEnumerator(cts.Token);
                    try
                    {
                        if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                        {
                            // We obtained at least one subscription — connectivity works.
                        }
                    }
                    finally
                    {
                        await enumerator.DisposeAsync().ConfigureAwait(false);
                    }

                    healthyCount++;
                    _logger.LogDebug("GCP health check passed for namespace {NamespaceId}", ns.Id);
                }
                catch (RpcException rpcEx) when (
                    rpcEx.StatusCode == StatusCode.NotFound ||
                    rpcEx.StatusCode == StatusCode.PermissionDenied)
                {
                    // Project reachable but no subscriptions / permissions — treat as healthy connectivity.
                    healthyCount++;
                    _logger.LogDebug(
                        "GCP health check: namespace {NamespaceId} reachable (status={Status})",
                        ns.Id, rpcEx.StatusCode);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    unhealthyNames.Add(ns.Name);
                    _logger.LogWarning(ex, "GCP health check failed for namespace {NamespaceId}", ns.Id);
                }
            }

            data["HealthyGcpNamespaces"] = healthyCount;
            data["UnhealthyGcpNamespaces"] = unhealthyNames.Count;

            if (unhealthyNames.Count > 0)
                data["UnhealthyGcpNamespaceNames"] = string.Join(", ", unhealthyNames);

            if (unhealthyNames.Count == gcpNamespaces.Count)
            {
                return HealthCheckResult.Unhealthy(
                    "All GCP Pub/Sub namespaces are unreachable.",
                    data: data);
            }

            if (unhealthyNames.Count > 0)
            {
                return HealthCheckResult.Degraded(
                    $"{unhealthyNames.Count} of {gcpNamespaces.Count} GCP namespaces are unreachable.",
                    data: data);
            }

            return HealthCheckResult.Healthy(
                $"All {gcpNamespaces.Count} GCP Pub/Sub namespaces are reachable.",
                data: data);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during GCP Pub/Sub health check");
            return HealthCheckResult.Unhealthy(
                "GCP Pub/Sub health check failed with an exception.",
                ex,
                data);
        }
    }
}
