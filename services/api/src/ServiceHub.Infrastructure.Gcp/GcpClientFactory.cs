using System.Collections.Concurrent;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.PubSub.V1;
using Grpc.Auth;
using Grpc.Core;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.Gcp;

/// <summary>
/// Creates GCP Pub/Sub <see cref="PublisherClient"/> and <see cref="SubscriberClient"/> instances
/// per namespace. Clients are cached by (namespaceId, entityId) to avoid expensive re-creation.
/// </summary>
/// <remarks>
/// Credential resolution:
/// <list type="number">
/// <item><description>
/// <see cref="ConnectionAuthType.GcpServiceAccount"/> — connection string is a Service Account JSON key
/// (the raw JSON, not a file path). Parses to <see cref="ServiceAccountCredential"/>.
/// </description></item>
/// <item><description>
/// <see cref="ConnectionAuthType.GcpWorkloadIdentity"/> — uses Application Default Credentials
/// (the explicit keyless opt-in).
/// </description></item>
/// <item><description>
/// Any other auth type fails closed with a configuration error — the host's ambient
/// identity is never borrowed implicitly.
/// </description></item>
/// </list>
/// </remarks>
public sealed class GcpClientFactory : IGcpClientFactory
{
    private readonly IConnectionStringProtector _protector;
    private readonly ILogger<GcpClientFactory> _logger;

    // Cache key: "{namespaceId}:{entityId}"
    private readonly ConcurrentDictionary<string, PublisherClient> _publisherCache = new();

    // SubscriberServiceApiClient/PublisherServiceApiClient expose no Dispose/Shutdown of their
    // own when built with explicit ChannelCredentials, so the underlying gRPC channel — the
    // thing actually holding the socket — is tracked alongside the client and shut down explicitly.
    private readonly ConcurrentDictionary<string, (SubscriberServiceApiClient Client, ChannelBase Channel)> _subscriberCache = new();
    private readonly ConcurrentDictionary<Guid, (PublisherServiceApiClient Client, ChannelBase Channel)> _topicAdminCache = new();

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
        _logger.LogDebug("Created PublisherClient for topic {TopicId} in project {ProjectId}",
            LogRedactor.SanitiseForLog(topicId), LogRedactor.SanitiseForLog(projectId));
        return client;
    }

    /// <inheritdoc/>
    public async Task<SubscriberServiceApiClient> GetSubscriberClientAsync(Namespace ns, string subscriptionId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentNullException.ThrowIfNull(subscriptionId);

        var cacheKey = $"{ns.Id}:{subscriptionId}";
        if (_subscriberCache.TryGetValue(cacheKey, out var cached))
            return cached.Client;

        var credential = await ResolveCredentialAsync(ns).ConfigureAwait(false);

        var clientBuilder = new SubscriberServiceApiClientBuilder
        {
            ChannelCredentials = credential.ToChannelCredentials()
        };

        var client = await clientBuilder.BuildAsync(ct).ConfigureAwait(false);

        _subscriberCache.TryAdd(cacheKey, (client, clientBuilder.LastCreatedChannel));
        var projectId = GetProjectId(ns);
        _logger.LogDebug("Created SubscriberServiceApiClient for subscription {SubscriptionId} in project {ProjectId}",
            LogRedactor.SanitiseForLog(subscriptionId), LogRedactor.SanitiseForLog(projectId));
        return client;
    }

    /// <inheritdoc/>
    public async Task<PublisherServiceApiClient> GetTopicAdminClientAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);

        if (_topicAdminCache.TryGetValue(ns.Id, out var cached))
            return cached.Client;

        var credential = await ResolveCredentialAsync(ns).ConfigureAwait(false);

        var clientBuilder = new PublisherServiceApiClientBuilder
        {
            ChannelCredentials = credential.ToChannelCredentials()
        };

        var client = await clientBuilder.BuildAsync(ct).ConfigureAwait(false);

        _topicAdminCache.TryAdd(ns.Id, (client, clientBuilder.LastCreatedChannel));
        _logger.LogDebug("Created PublisherServiceApiClient (topic admin) for project {ProjectId}",
            LogRedactor.SanitiseForLog(GetProjectId(ns)));
        return client;
    }

    /// <inheritdoc/>
    public async Task RemoveClientAsync(Guid namespaceId, CancellationToken cancellationToken = default)
    {
        var prefix = $"{namespaceId}:";

        // Bound the shutdown wait but still honour the caller's cancellation.
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        foreach (var key in _publisherCache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            if (_publisherCache.TryRemove(key, out var publisher))
            {
                _logger.LogInformation("Shutting down PublisherClient for namespace {NamespaceId}", namespaceId);
                try
                {
                    await publisher.ShutdownAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error shutting down PublisherClient for namespace {NamespaceId}", namespaceId);
                }
            }
        }

        foreach (var key in _subscriberCache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            if (_subscriberCache.TryRemove(key, out var entry))
            {
                _logger.LogInformation("Shutting down SubscriberServiceApiClient channel for namespace {NamespaceId}", namespaceId);
                try
                {
                    await entry.Channel.ShutdownAsync().WaitAsync(linkedCts.Token).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error shutting down SubscriberServiceApiClient channel for namespace {NamespaceId}", namespaceId);
                }
            }
        }

        if (_topicAdminCache.TryRemove(namespaceId, out var topicAdminEntry))
        {
            _logger.LogInformation("Shutting down topic-admin client channel for namespace {NamespaceId}", namespaceId);
            try
            {
                await topicAdminEntry.Channel.ShutdownAsync().WaitAsync(linkedCts.Token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error shutting down topic-admin client channel for namespace {NamespaceId}", namespaceId);
            }
        }
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
        switch (ns.AuthType)
        {
            // GcpServiceAccount = Service Account JSON key stored in connection string
            // (the auth type Namespace.Create assigns to GCP namespaces). Fails closed:
            // a missing or unparsable key is a configuration error, never an ADC fallback.
            case ConnectionAuthType.GcpServiceAccount:
            {
                if (string.IsNullOrWhiteSpace(ns.ConnectionString))
                    throw new InvalidOperationException(
                        $"GcpServiceAccount namespace {ns.Id} has no Service Account key in ConnectionString.");

                var unprotected = _protector.Unprotect(ns.ConnectionString);
                if (unprotected.IsFailure)
                    throw new InvalidOperationException(
                        $"Failed to decrypt the Service Account key for namespace {ns.Id}: {unprotected.Error.Message}");

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
                    throw new InvalidOperationException(
                        $"The Service Account key for namespace {ns.Id} is not a valid Google credential.", ex);
                }
            }

            // Workload Identity Federation is the explicit keyless opt-in for ADC.
            case ConnectionAuthType.GcpWorkloadIdentity:
            {
                _logger.LogDebug("Using Application Default Credentials for namespace {NamespaceId}", ns.Id);
                var adc = await GoogleCredential.GetApplicationDefaultAsync().ConfigureAwait(false);
                return adc.CreateScoped(["https://www.googleapis.com/auth/pubsub"]);
            }

            // Fail closed: any other auth type is a configuration error — never borrow
            // the host's ambient identity for a namespace that didn't opt into it.
            default:
                throw new InvalidOperationException(
                    $"Namespace {ns.Id} has auth type {ns.AuthType}, which is not a supported GCP auth type. " +
                    "Expected GcpServiceAccount or GcpWorkloadIdentity.");
        }
    }
}
