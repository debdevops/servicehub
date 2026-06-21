using Google.Api.Gax.ResourceNames;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Gcp;

/// <summary>
/// Implements <see cref="ICloudMessagingProvider"/> for GCP Pub/Sub.
/// </summary>
public sealed class GcpMessagingProvider : ICloudMessagingProvider
{
    private readonly IGcpClientFactory _clientFactory;
    private readonly GcpMessageReceiver _receiver;
    private readonly GcpMessageSender _sender;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<GcpMessagingProvider> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="GcpMessagingProvider"/>.
    /// </summary>
    /// <param name="clientFactory">Factory for creating Pub/Sub clients.</param>
    /// <param name="receiver">The GCP message receiver.</param>
    /// <param name="sender">The GCP message sender.</param>
    /// <param name="namespaceRepository">Repository for namespace lookups.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public GcpMessagingProvider(
        IGcpClientFactory clientFactory,
        GcpMessageReceiver receiver,
        GcpMessageSender sender,
        INamespaceRepository namespaceRepository,
        ILogger<GcpMessagingProvider> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public CloudProviderType ProviderType => CloudProviderType.Gcp;

    /// <inheritdoc/>
    public IMessageReceiver GetMessageReceiver() => _receiver;

    /// <inheritdoc/>
    public IMessageSender GetMessageSender() => _sender;

    /// <inheritdoc/>
    public async Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        _logger.LogDebug("Validating GCP Pub/Sub connection for namespace {NamespaceId}", ns.Id);

        if (string.IsNullOrWhiteSpace(ns.GcpProjectId))
        {
            return Result.Failure(Error.Validation("GCP.PubSub.NoProjectId",
                "GCP Project ID is required. Set GcpProjectId on the namespace."));
        }

        // Validate service-account JSON shape before making any network call.
        // A valid Google service-account key file must be a JSON object that contains
        // at minimum the fields: type, project_id, private_key_id, private_key, client_email.
        if (!string.IsNullOrWhiteSpace(ns.ConnectionString))
        {
            var shapeError = ValidateServiceAccountJsonShape(ns.ConnectionString);
            if (shapeError is not null)
                return shapeError;
        }

        try
        {
            // CRITICAL FIX: Use namespace credentials (service-account JSON or ADC) via the
            // client factory — NOT PublisherServiceApiClient.CreateAsync() which uses the
            // host's ADC and ignores the user-provided connection string entirely.
            //
            // We reuse GetSubscriberClientAsync here because it resolves the credential
            // correctly and a ListSubscriptions call is cheaper than ListTopics for a probe.
            var client = await _clientFactory.GetSubscriberClientAsync(
                ns, "_servicehub_validate_probe_", ct).ConfigureAwait(false);

            var listRequest = new Google.Cloud.PubSub.V1.ListSubscriptionsRequest
            {
                Project = $"projects/{ns.GcpProjectId}"
            };
            var enumerator = client.ListSubscriptionsAsync(listRequest).GetAsyncEnumerator(ct);
            try
            {
                // Any response (even empty) means credentials are valid and the project is reachable.
                await enumerator.MoveNextAsync().ConfigureAwait(false);
            }
            finally
            {
                await enumerator.DisposeAsync().ConfigureAwait(false);
            }

            _logger.LogInformation("GCP Pub/Sub connection validated for namespace {NamespaceId}", ns.Id);
            return Result.Success();
        }
        catch (Grpc.Core.RpcException ex) when (ex.Status.StatusCode is Grpc.Core.StatusCode.Unauthenticated or Grpc.Core.StatusCode.PermissionDenied)
        {
            _logger.LogWarning("GCP auth validation failed for namespace {NamespaceId}: {Status}", ns.Id, ex.Status);
            return Result.Failure(Error.Validation("GCP.PubSub.AuthFailed",
                $"GCP credential validation failed: {ex.Status.Detail}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating GCP connection for namespace {NamespaceId}", ns.Id);
            return Result.Failure(Error.ExternalService("GCP.PubSub.ValidationFailed", ex.Message));
        }
    }

    /// <summary>
    /// Validates that the provided JSON string has the shape of a Google service-account key file.
    /// Returns a <see cref="Result"/> failure when the JSON is malformed or missing required fields;
    /// returns <c>null</c> when the shape is acceptable.
    /// </summary>
    private static Result? ValidateServiceAccountJsonShape(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
                return Result.Failure(Error.Validation("GCP.ServiceAccount.NotJsonObject",
                    "The GCP service-account credential must be a JSON object."));

            // Check the 'type' field so we give a clear error if someone pastes an OAuth token
            // or an ADC credentials file instead of a service-account key.
            if (root.TryGetProperty("type", out var typeEl) &&
                !string.Equals(typeEl.GetString(), "service_account", StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure(Error.Validation("GCP.ServiceAccount.WrongType",
                    $"Expected credential type 'service_account', but got '{typeEl.GetString()}'. " +
                    "Download a Service Account key JSON from the GCP IAM console."));
            }

            string[] required = ["type", "project_id", "private_key_id", "private_key", "client_email"];
            var missing = required.Where(f => !root.TryGetProperty(f, out _)).ToArray();
            if (missing.Length > 0)
                return Result.Failure(Error.Validation("GCP.ServiceAccount.MissingFields",
                    $"Service-account JSON is missing required fields: {string.Join(", ", missing)}. " +
                    "Download a complete Service Account key JSON from the GCP IAM console."));

            return null; // shape is valid
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Result.Failure(Error.Validation("GCP.ServiceAccount.InvalidJson",
                $"The GCP service-account credential is not valid JSON: {ex.Message}"));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(Guid namespaceId, CancellationToken ct)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, ct).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<IReadOnlyList<CloudEntity>>(nsResult.Error);

        var ns = nsResult.Value;

        if (string.IsNullOrWhiteSpace(ns.GcpProjectId))
            return Result.Failure<IReadOnlyList<CloudEntity>>(Error.Validation(
                "GCP.PubSub.NoProjectId", "GCP Project ID is required."));

        var entities = new List<CloudEntity>();

        try
        {
            // CRITICAL FIX: Resolve credentials via the client factory (namespace service-account
            // JSON or ADC), not PublisherServiceApiClient.CreateAsync() which uses host ADC only.
            var subscriberClient = await _clientFactory.GetSubscriberClientAsync(
                ns, "_servicehub_list_probe_", ct).ConfigureAwait(false);
            var publisherClient = await _clientFactory.GetPublisherClientAsync(
                ns, "_servicehub_list_probe_", ct).ConfigureAwait(false);
            var project = new ProjectName(ns.GcpProjectId);

            // List subscriptions using the subscriber client
            var subRequest = new Google.Cloud.PubSub.V1.ListSubscriptionsRequest
            {
                Project = $"projects/{ns.GcpProjectId}"
            };
            await foreach (var sub in subscriberClient.ListSubscriptionsAsync(subRequest).WithCancellation(ct))
            {
                entities.Add(new CloudEntity
                {
                    Name = sub.SubscriptionName.SubscriptionId,
                    EntityType = "Subscription",
                    Provider = CloudProviderType.Gcp
                });
            }

            return Result.Success<IReadOnlyList<CloudEntity>>(entities);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error listing GCP Pub/Sub entities for namespace {NamespaceId}", namespaceId);
            return Result.Failure<IReadOnlyList<CloudEntity>>(
                Error.ExternalService("GCP.PubSub.ListFailed", ex.Message));
        }
    }
}
