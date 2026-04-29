using System.Text;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;
using Utf8Encoding = System.Text.Encoding;

namespace ServiceHub.Infrastructure.Gcp;

/// <summary>
/// Implements <see cref="IMessageSender"/> for GCP Pub/Sub topics.
/// <para>
/// Message ordering: when <see cref="SendMessageRequest.SessionId"/> is set, it is used as the
/// <see cref="PubsubMessage.OrderingKey"/>. The <see cref="PublisherClient"/> must be configured
/// with <c>EnableMessageOrdering = true</c> for ordering to take effect.
/// </para>
/// </summary>
public sealed class GcpMessageSender : IMessageSender
{
    private readonly IGcpClientFactory _clientFactory;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<GcpMessageSender> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="GcpMessageSender"/>.
    /// </summary>
    /// <param name="clientFactory">Factory that creates Pub/Sub publisher clients per namespace.</param>
    /// <param name="namespaceRepository">Repository for resolving namespace credentials by ID.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public GcpMessageSender(
        IGcpClientFactory clientFactory,
        INamespaceRepository namespaceRepository,
        ILogger<GcpMessageSender> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<Result> SendAsync(
        SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.NamespaceId is null || request.EntityName is null)
            return Result.Failure(Error.Validation("GCP.PubSub.InvalidRequest",
                "NamespaceId and EntityName are required."));

        var nsResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure(nsResult.Error);

        try
        {
            var publisher = await _clientFactory.GetPublisherClientAsync(
                nsResult.Value, request.EntityName, cancellationToken).ConfigureAwait(false);

            var message = BuildPubSubMessage(request);
            var messageId = await publisher.PublishAsync(message).ConfigureAwait(false);

            _logger.LogInformation("Published Pub/Sub message {MessageId} to topic {TopicId}", messageId, request.EntityName);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error publishing Pub/Sub message to topic {TopicId}", request.EntityName);
            return Result.Failure(Error.ExternalService("GCP.PubSub.SendFailed", ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> SendBatchAsync(
        IEnumerable<SendMessageRequest> requests,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requests);

        var requestList = requests.ToList();
        if (requestList.Count == 0)
            return Result.Success();

        var first = requestList[0];
        if (first.NamespaceId is null || first.EntityName is null)
            return Result.Failure(Error.Validation("GCP.PubSub.InvalidRequest",
                "NamespaceId and EntityName are required."));

        var nsResult = await _namespaceRepository.GetByIdAsync(first.NamespaceId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure(nsResult.Error);

        try
        {
            var publisher = await _clientFactory.GetPublisherClientAsync(
                nsResult.Value, first.EntityName, cancellationToken).ConfigureAwait(false);

            // Pub/Sub batches are managed internally by PublisherClient (auto-batching).
            // Publish all concurrently — the client handles actual wire batching.
            var tasks = requestList.Select(req => publisher.PublishAsync(BuildPubSubMessage(req)));
            await Task.WhenAll(tasks).ConfigureAwait(false);

            _logger.LogInformation("Batch published {Count} Pub/Sub messages to topic {TopicId}",
                requestList.Count, first.EntityName);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error batch publishing Pub/Sub messages to topic {TopicId}", first.EntityName);
            return Result.Failure(Error.ExternalService("GCP.PubSub.BatchSendFailed", ex.Message));
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static PubsubMessage BuildPubSubMessage(SendMessageRequest request)
    {
        var message = new PubsubMessage
        {
            Data = ByteString.CopyFrom(Utf8Encoding.UTF8.GetBytes(request.Body ?? string.Empty))
        };

        // Map SessionId to OrderingKey for ordered delivery
        if (!string.IsNullOrWhiteSpace(request.SessionId))
            message.OrderingKey = request.SessionId;

        // Map ApplicationProperties to PubsubMessage.Attributes
        if (request.ApplicationProperties is { Count: > 0 })
        {
            foreach (var kvp in request.ApplicationProperties)
            {
                if (kvp.Value is not null)
                    message.Attributes[kvp.Key] = kvp.Value.ToString() ?? string.Empty;
            }
        }

        return message;
    }
}
