using System.Collections.Concurrent;
using System.Text;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Gcp.Models;
using ServiceHub.Shared.Results;
using Utf8Enc = System.Text.Encoding;

namespace ServiceHub.Infrastructure.Gcp;

/// <summary>
/// Implements <see cref="IMessageReceiver"/> for GCP Pub/Sub subscriptions.
/// <para>
/// Peek behaviour: Pub/Sub has no native non-destructive read. This implementation uses
/// <c>ModifyAckDeadline(0)</c> — messages are pulled then their ack deadline is immediately
/// reset to zero, making them immediately re-deliverable. This means no consumer is blocked
/// and no message is acknowledged.
/// </para>
/// </summary>
public sealed class GcpMessageReceiver : IMessageReceiver, IAckDeadlineStatusProvider
{
    private readonly IGcpClientFactory _clientFactory;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<GcpMessageReceiver> _logger;

    // Maps synthetic sequence number → ack ID for replay/purge operations.
    // ConcurrentDictionary ensures thread-safe access from parallel async continuations.
    private readonly ConcurrentDictionary<long, string> _ackIdCache = new();

    /// <summary>Maximum number of ack-ID cache entries before eviction is triggered.</summary>
    private const int AckIdCacheMaxSize = 50_000;

    /// <summary>Number of oldest entries to evict when the cache reaches <see cref="AckIdCacheMaxSize"/>.</summary>
    private const int AckIdCacheEvictCount = 1_000;

    /// <summary>
    /// Initialises a new instance of <see cref="GcpMessageReceiver"/>.
    /// </summary>
    /// <param name="clientFactory">Factory that creates Pub/Sub clients per namespace.</param>
    /// <param name="namespaceRepository">Repository for resolving namespace credentials by ID.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public GcpMessageReceiver(
        IGcpClientFactory clientFactory,
        INamespaceRepository namespaceRepository,
        ILogger<GcpMessageReceiver> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── IMessageReceiver ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Message>>> PeekMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nsResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<IReadOnlyList<Message>>(nsResult.Error);

        var ns = nsResult.Value;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var subscriber = await _clientFactory.GetSubscriberClientAsync(
                ns, request.EntityName, linkedCts.Token).ConfigureAwait(false);

            var subResourceName = GetSubscriptionResourceName(ns, request.EntityName);
            var messages = await PullAndNackAsync(subscriber, subResourceName, request.MaxMessages, linkedCts.Token).ConfigureAwait(false);
            var mapped = MapToMessages(messages, request.NamespaceId, request.EntityName, fromDlq: false);

            _logger.LogDebug("Peeked {Count} messages from Pub/Sub subscription {Subscription}", mapped.Count, request.EntityName);
            return Result.Success<IReadOnlyList<Message>>(mapped);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("GCP Pub/Sub peek timed out after {Seconds}s for subscription {Subscription}", OperationTimeoutSeconds, request.EntityName);
            return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService(
                "GCP.PubSub.Timeout", $"Pub/Sub operation timed out after {OperationTimeoutSeconds}s."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error peeking Pub/Sub messages from {Subscription}", request.EntityName);
            return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService("GCP.PubSub.PeekFailed", ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // In GCP Pub/Sub, dead-letter messages are forwarded to a separate dead-letter topic's subscription.
        // Convention: the dead-letter subscription is named "{subscriptionId}-dlq" or passed explicitly.
        var dlqSubscription = $"{request.EntityName}-dlq";

        var nsResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<IReadOnlyList<Message>>(nsResult.Error);

        var ns = nsResult.Value;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var subscriber = await _clientFactory.GetSubscriberClientAsync(
                ns, dlqSubscription, linkedCts.Token).ConfigureAwait(false);

            var subResourceName = GetSubscriptionResourceName(ns, dlqSubscription);
            var messages = await PullAndNackAsync(subscriber, subResourceName, request.MaxMessages, linkedCts.Token).ConfigureAwait(false);
            var mapped = MapToMessages(messages, request.NamespaceId, request.EntityName, fromDlq: true);

            _logger.LogDebug("Peeked {Count} DLQ messages from Pub/Sub subscription {Subscription}", mapped.Count, dlqSubscription);
            return Result.Success<IReadOnlyList<Message>>(mapped);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("GCP Pub/Sub DLQ peek timed out after {Seconds}s for subscription {Subscription}", OperationTimeoutSeconds, dlqSubscription);
            return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService(
                "GCP.PubSub.Timeout", $"Pub/Sub DLQ operation timed out after {OperationTimeoutSeconds}s."));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error peeking Pub/Sub DLQ messages from {Subscription}", dlqSubscription);
            return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService("GCP.PubSub.DlqPeekFailed", ex.Message));
        }
    }

    // Hard limit per individual Pub/Sub API call.
    private const int OperationTimeoutSeconds = 15;

    /// <inheritdoc/>
    public Task<Result<long>> GetMessageCountAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName = null,
        CancellationToken cancellationToken = default)
    {
        // Pub/Sub does not expose a direct message count API.
        // Return a dedicated error code so the UI can render "N/A" instead of -1 or a spinner.
        _logger.LogDebug(
            "GCP Pub/Sub message count is unavailable via API. Subscription: {Subscription}",
            entityName);
        return Task.FromResult(Result.Failure<long>(Error.Validation(
            "GCP.PubSub.CountUnavailable",
            "GCP Pub/Sub does not support direct message count queries. " +
            "Use the Cloud Monitoring API or console for subscription metrics.")));
    }

    /// <inheritdoc/>
    public async Task<Result<int>> DeadLetterMessagesAsync(
        DeadLetterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Pub/Sub DLQ is policy-driven (MaxDeliveryAttempts). Manual DLQ is achieved by
        // nacking a message until it reaches the MaxDeliveryAttempts threshold.
        // Simulate by simply acknowledging-and-discarding from the main subscription (not ideal)
        // and logging a warning. Real implementations should configure the dead-letter policy.
        _logger.LogWarning(
            "GCP Pub/Sub does not support direct dead-lettering. " +
            "Configure a dead-letter policy on subscription {Subscription} with MaxDeliveryAttempts.",
            request.EntityName);
        return await Task.FromResult(Result.Failure<int>(Error.Validation(
            "GCP.PubSub.NoManualDlq",
            "GCP Pub/Sub requires a dead-letter policy to be configured on the subscription. " +
            "Manual dead-lettering is not supported. Set MaxDeliveryAttempts on the subscription."))).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<Result> ReplayMessageAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        if (!_ackIdCache.TryGetValue(sequenceNumber, out var ackId))
        {
            return Result.Failure(Error.NotFound("GCP.PubSub.MessageNotFound",
                $"Message {sequenceNumber} not found in ack ID cache. Peek the subscription first."));
        }

        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure(nsResult.Error);

        try
        {
            var subscriber = await _clientFactory.GetSubscriberClientAsync(
                nsResult.Value, entityName, cancellationToken).ConfigureAwait(false);

            var subResourceName = GetSubscriptionResourceName(nsResult.Value, entityName);
            // Nack the message (ack deadline 0) — forces immediate redelivery
            await subscriber.ModifyAckDeadlineAsync(new ModifyAckDeadlineRequest
            {
                Subscription = subResourceName,
                AckIds = { ackId },
                AckDeadlineSeconds = 0
            }, cancellationToken).ConfigureAwait(false);

            _ackIdCache.TryRemove(sequenceNumber, out _);
            _logger.LogInformation("Replayed Pub/Sub message {Seq} on subscription {Subscription}", sequenceNumber, entityName);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error replaying Pub/Sub message {Seq} for {Subscription}", sequenceNumber, entityName);
            return Result.Failure(Error.ExternalService("GCP.PubSub.ReplayFailed", ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> PurgeMessageAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        bool fromDeadLetter,
        CancellationToken cancellationToken = default)
    {
        if (!_ackIdCache.TryGetValue(sequenceNumber, out var ackId))
        {
            return Result.Failure(Error.NotFound("GCP.PubSub.MessageNotFound",
                $"Message {sequenceNumber} not found in ack ID cache. Peek the subscription first."));
        }

        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure(nsResult.Error);

        var subscriptionId = fromDeadLetter ? $"{entityName}-dlq" : entityName;

        try
        {
            var subscriber = await _clientFactory.GetSubscriberClientAsync(
                nsResult.Value, subscriptionId, cancellationToken).ConfigureAwait(false);

            var subResourceName = GetSubscriptionResourceName(nsResult.Value, subscriptionId);
            // Acknowledge = permanently delete from subscription
            await subscriber.AcknowledgeAsync(new AcknowledgeRequest
            {
                Subscription = subResourceName,
                AckIds = { ackId }
            }, cancellationToken).ConfigureAwait(false);

            _ackIdCache.TryRemove(sequenceNumber, out _);
            _logger.LogInformation("Purged Pub/Sub message {Seq} from {Subscription}", sequenceNumber, subscriptionId);
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error purging Pub/Sub message {Seq} from {Subscription}", sequenceNumber, subscriptionId);
            return Result.Failure(Error.ExternalService("GCP.PubSub.PurgeFailed", ex.Message));
        }
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<Message>>> GetScheduledMessagesAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "GCP Pub/Sub does not support scheduled message inspection. " +
            "Use Cloud Tasks or Cloud Scheduler for scheduled delivery. Subscription: {Subscription}",
            entityName);
        return Task.FromResult(Result.Success<IReadOnlyList<Message>>(Array.Empty<Message>()));
    }

    // ── GCP-specific public features ──────────────────────────────────────────

    /// <summary>
    /// Returns the ack-deadline and dead-letter policy configuration for a subscription.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="subscriptionId">The subscription ID.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="GcpAckDeadlineStatus"/> snapshot for the subscription.</returns>
    public async Task<Result<GcpAckDeadlineStatus>> GetAckDeadlineStatusAsync(
        Guid namespaceId,
        string subscriptionId,
        CancellationToken cancellationToken = default)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<GcpAckDeadlineStatus>(nsResult.Error);

        try
        {
            var subscriber = await _clientFactory.GetSubscriberClientAsync(
                nsResult.Value, subscriptionId, cancellationToken).ConfigureAwait(false);

            var subResourceName = GetSubscriptionResourceName(nsResult.Value, subscriptionId);
            var sub = await subscriber.GetSubscriptionAsync(
                subResourceName, cancellationToken).ConfigureAwait(false);

            var hasDlp = sub.DeadLetterPolicy is not null;
            return Result.Success(new GcpAckDeadlineStatus(
                sub.AckDeadlineSeconds,
                hasDlp,
                sub.DeadLetterPolicy?.DeadLetterTopic,
                hasDlp ? sub.DeadLetterPolicy!.MaxDeliveryAttempts : null,
                sub.EnableMessageOrdering));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error getting ack deadline status for {Subscription}", subscriptionId);
            return Result.Failure<GcpAckDeadlineStatus>(Error.ExternalService("GCP.PubSub.AckStatusFailed", ex.Message));
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private async Task<List<ReceivedMessage>> PullAndNackAsync(
        SubscriberServiceApiClient subscriber, string subscriptionResourceName, int maxMessages, CancellationToken ct)
    {
        var pullRequest = new PullRequest
        {
            Subscription = subscriptionResourceName,
            MaxMessages = maxMessages
        };

        var response = await subscriber.PullAsync(pullRequest, ct).ConfigureAwait(false);
        var messages = response.ReceivedMessages.ToList();

        if (messages.Count > 0)
        {
            // ModifyAckDeadline(0) = immediately re-queue (peek pattern)
            await subscriber.ModifyAckDeadlineAsync(new ModifyAckDeadlineRequest
            {
                Subscription = subscriptionResourceName,
                AckIds = { messages.Select(m => m.AckId) },
                AckDeadlineSeconds = 0
            }, ct).ConfigureAwait(false);
        }

        return messages;
    }

    private List<Message> MapToMessages(
        IReadOnlyList<ReceivedMessage> gcpMessages,
        Guid namespaceId,
        string entityName,
        bool fromDlq)
    {
        var mapped = new List<Message>(gcpMessages.Count);

        foreach (var received in gcpMessages)
        {
            var msg = received.Message;
            var seqNum = ComputeSequenceNumber(received.AckId);
            if (_ackIdCache.Count >= AckIdCacheMaxSize)
            {
                var toEvict = _ackIdCache.Keys.Take(AckIdCacheEvictCount).ToList();
                foreach (var evictKey in toEvict)
                    _ackIdCache.TryRemove(evictKey, out _);
            }
            _ackIdCache.TryAdd(seqNum, received.AckId);

            var body = msg.Data?.IsEmpty == false
                ? Utf8Enc.UTF8.GetString(msg.Data.ToByteArray())
                : string.Empty;

            var appProps = msg.Attributes.Count > 0
                ? msg.Attributes.ToDictionary(k => k.Key, v => (object)v.Value)
                : null;

            mapped.Add(new Message
            {
                MessageId = msg.MessageId,
                SequenceNumber = seqNum,
                Body = body,
                DeliveryCount = received.DeliveryAttempt,
                EnqueuedTime = msg.PublishTime?.ToDateTimeOffset() ?? DateTimeOffset.UtcNow,
                ApplicationProperties = appProps is { Count: > 0 }
                    ? appProps as IReadOnlyDictionary<string, object>
                    : null,
                NamespaceId = namespaceId,
                EntityName = entityName,
                IsFromDeadLetter = fromDlq,
                State = MessageState.Active
            });
        }

        return mapped;
    }

    private static long ComputeSequenceNumber(string ackId) =>
        Math.Abs((long)ackId.GetHashCode());

    private static string GetSubscriptionResourceName(Core.Entities.Namespace ns, string subscriptionId)
    {
        var projectId = ns.GcpProjectId ?? "unknown-project";
        return SubscriptionName.FromProjectSubscription(projectId, subscriptionId).ToString();
    }
}
