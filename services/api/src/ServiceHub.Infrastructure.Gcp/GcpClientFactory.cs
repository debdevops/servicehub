using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.PubSub.V1;
using Grpc.Auth;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.Gcp;

/// <summary>
/// Creates GCP Pub/Sub <see cref="PublisherClient"/> and <see cref="SubscriberClient"/> instances
/// per namespace. Clients are cached by (namespaceId, entityId) to avoid expensive re-creation.
/// </summary>
/// <remarks>
/// Credential resolution order:
/// <list type="number">
/// <item><description>
/// <see cref="ConnectionAuthType.ServicePrincipal"/> — connection string is a Service Account JSON key
/// (the raw JSON, not a file path). Parses to <see cref="ServiceAccountCredential"/>.
/// </description></item>
/// <item><description>
/// Any other auth type — uses Application Default Credentials (Workload Identity,
/// gcloud ADC, etc.).
/// </description></item>
/// </list>
/// </remarks>
public sealed class GcpClientFactory : IGcpClientFactory
{
    private readonly IConnectionStringProtector _protector;
    private readonly ILogger<GcpClientFactory> _logger;

    // Cache key: "{namespaceId}:{entityId}"
    private readonly ConcurrentDictionary<string, PublisherClient> _publisherCache = new();
    private readonly ConcurrentDictionary<string, SubscriberServiceApiClient> _subscriberCache = new();

    /// <summary>
    /// Initialises a new instance of <see cref="GcpClientFactory"/>.
    /// </summary>
    /// <param name="protector">Decrypts stored connection strings (Service Account JSON).</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public GcpClientFactory(IConnectionStringProtector protector, ILogger<GcpClientFactory> logger)
    {
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<PublisherClient> GetPublisherClientAsync(Namespace ns, string topicId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentNullException.ThrowIfNull(topicId);

        var cacheKey = $"{ns.Id}:{topicId}";
        if (_publisherCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var projectId = GetProjectId(ns);
        var topicName = TopicName.FromProjectTopic(projectId, topicId);
        var credential = await ResolveCredentialAsync(ns).ConfigureAwait(false);

        var clientBuilder = new PublisherClientBuilder
        {
            TopicName = topicName,
            ChannelCredentials = credential.ToChannelCredentials()
        };

        var client = await clientBuilder.BuildAsync(ct).ConfigureAwait(false);

        _publisherCache.TryAdd(cacheKey, client);
        _logger.LogDebug("Created PublisherClient for topic {TopicId} in project {ProjectId}", topicId, projectId);
        return client;
    }

    /// <inheritdoc/>
    public async Task<SubscriberServiceApiClient> GetSubscriberClientAsync(Namespace ns, string subscriptionId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentNullException.ThrowIfNull(subscriptionId);

        var cacheKey = $"{ns.Id}:{subscriptionId}";
        if (_subscriberCache.TryGetValue(cacheKey, out var cached))
            return cached;

        var credential = await ResolveCredentialAsync(ns).ConfigureAwait(false);

        var clientBuilder = new SubscriberServiceApiClientBuilder
        {
            ChannelCredentials = credential.ToChannelCredentials()
        };

        var client = await clientBuilder.BuildAsync(ct).ConfigureAwait(false);

        _subscriberCache.TryAdd(cacheKey, client);
        var projectId = GetProjectId(ns);
        _logger.LogDebug("Created SubscriberServiceApiClient for subscription {SubscriptionId} in project {ProjectId}",
            subscriptionId, projectId);
        return client;
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private string GetProjectId(Namespace ns)
    {
        if (!string.IsNullOrWhiteSpace(ns.GcpProjectId))
            return ns.GcpProjectId;

        // Fall back to parsing from connection string as "projectId=my-gcp-project"
        if (!string.IsNullOrWhiteSpace(ns.ConnectionString))
        {
            var unprotected = _protector.Unprotect(ns.ConnectionString);
            if (unprotected.IsSuccess)
            {
                var match = System.Text.RegularExpressions.Regex.Match(
                    unprotected.Value, @"projectId=([^;]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                    return match.Groups[1].Value.Trim();
            }
        }

        throw new InvalidOperationException(
            $"GCP project ID not found in namespace {ns.Id}. " +
            "Set GcpProjectId or include 'projectId=...' in the connection string.");
    }

    private async Task<GoogleCredential> ResolveCredentialAsync(Namespace ns)
    {
        // ServicePrincipal = Service Account JSON key stored in connection string
        if (ns.AuthType == ConnectionAuthType.ServicePrincipal && !string.IsNullOrWhiteSpace(ns.ConnectionString))
        {
            var unprotected = _protector.Unprotect(ns.ConnectionString);
            if (unprotected.IsSuccess)
            {
                try
                {
                    using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(unprotected.Value));
                    var credential = GoogleCredential.FromStream(stream)
                        .CreateScoped(["https://www.googleapis.com/auth/pubsub"]);

                    _logger.LogDebug("Resolved Service Account credential for namespace {NamespaceId}", ns.Id);
                    return credential;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse Service Account JSON for namespace {NamespaceId}", ns.Id);
                }
            }
        }

        // Workload Identity / ADC fallback
        _logger.LogDebug("Using Application Default Credentials for namespace {NamespaceId}", ns.Id);
        var adc = await GoogleCredential.GetApplicationDefaultAsync().ConfigureAwait(false);
        return adc.CreateScoped(["https://www.googleapis.com/auth/pubsub"]);
    }
}
