using System.Collections.Concurrent;
using System.Text;
using Google.Api.Gax.ResourceNames;
using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using Polly;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Gcp.Models;
using ServiceHub.Infrastructure.Gcp.Resilience;
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
/// <para>
/// Sequence numbers are a stable hash of each message's <c>MessageId</c> (not its ack ID, which
/// is single-delivery and rotates on every pull). Replay/purge don't rely on any cross-request
/// cache of a message's ack ID — background DLQ scanning (see <c>DlqMonitorWorker</c>) pulls the
/// same subscriptions on its own schedule, nacking whatever it finds, which would otherwise
/// invalidate a cached ack ID before a user acts on it. Instead, replay/purge re-scan the target
/// subscription for the matching <c>MessageId</c> hash at act-time and use its current ack ID —
/// the same design AWS uses for the equivalent SQS receipt-handle problem.
/// </para>
/// </summary>
public sealed class GcpMessageReceiver : IMessageReceiver, IAckDeadlineStatusProvider
{
    private readonly IGcpClientFactory _clientFactory;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<GcpMessageReceiver> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    // Serializes scans (FindAndLockMessageAsync) per subscription so a replay/purge scan and a
    // concurrent DlqMonitorWorker tick against the same subscription don't race each other's
    // ack IDs. Static for the same reason AWS's _queueScanGates is: this receiver is AddScoped
    // (a new instance per HTTP request), but the gate must hold across the whole process.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _scanGates = new();

    private const int MaxScanBatches = 5;
    private const int ScanBatchSize = 100;
    private const int ScanLockSeconds = 30;

    private static SemaphoreSlim GetScanGate(string subscriptionResourceName) =>
        _scanGates.GetOrAdd(subscriptionResourceName, static _ => new SemaphoreSlim(1, 1));

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
        _resiliencePipeline = GcpResiliencePipeline.Create(_logger);
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

        // Topic/subscription-shaped requests (mirroring Azure/AWS addressing) carry the
        // subscription in SubscriptionName and the topic in EntityName; GCP has no
        // separate topic read path, so the subscription is what we actually pull from.
        var subscriptionId = request.SubscriptionName ?? request.EntityName;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var mapped = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var subscriber = await _clientFactory.GetSubscriberClientAsync(
                    ns, subscriptionId, ct).ConfigureAwait(false);

                var subResourceName = GetSubscriptionResourceName(ns, subscriptionId);
                var messages = await PullAndNackAsync(subscriber, subResourceName, request.MaxMessages, ct).ConfigureAwait(false);
                return MapToMessages(messages, request.NamespaceId, request.EntityName, fromDlq: false);
            }, linkedCts.Token).ConfigureAwait(false);

            _logger.LogDebug("Peeked {Count} messages from Pub/Sub subscription {Subscription}", mapped.Count, SanitizeForLog(request.EntityName));
            return Result.Success<IReadOnlyList<Message>>(mapped);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("GCP Pub/Sub peek timed out after {Seconds}s for subscription {Subscription}", OperationTimeoutSeconds, SanitizeForLog(request.EntityName));
            return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService(
                "GCP.PubSub.Timeout", $"Pub/Sub operation timed out after {OperationTimeoutSeconds}s."));
        }
        catch (Grpc.Core.RpcException ex) when (ex.Status.StatusCode == Grpc.Core.StatusCode.FailedPrecondition)
        {
            _logger.LogWarning(ex, "Pub/Sub subscription {Subscription} does not support Pull", SanitizeForLog(subscriptionId));
            return Result.Failure<IReadOnlyList<Message>>(UnsupportedSubscriptionTypeError(subscriptionId));
        }
        catch (Grpc.Core.RpcException ex) when (ex.Status.StatusCode == Grpc.Core.StatusCode.Cancelled)
        {
            // gRPC wraps cancellation as RpcException(Cancelled) rather than a plain
            // OperationCanceledException, for both causes linkedCts can carry: our own
            // OperationTimeoutSeconds elapsing, or the caller (browser) disconnecting/superseding
            // this request. The catch above never actually matches a gRPC-originated cancellation
            // because of that wrapping, so both cases land here and are told apart the same way.
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("GCP Pub/Sub peek timed out after {Seconds}s for subscription {Subscription}", OperationTimeoutSeconds, SanitizeForLog(request.EntityName));
                return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService(
                    "GCP.PubSub.Timeout", $"Pub/Sub operation timed out after {OperationTimeoutSeconds}s."));
            }

            // The client (browser) disconnected or superseded this request. Rethrow as
            // OperationCanceledException so ErrorHandlingMiddleware's existing client-disconnect
            // handling (499, no error-level log) takes over instead of this becoming a misleading 502.
            throw new OperationCanceledException("Pub/Sub peek cancelled by client.", ex, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error peeking Pub/Sub messages from {Subscription}", SanitizeForLog(request.EntityName));
            return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService("GCP.PubSub.PeekFailed", ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<Message>>> PeekDeadLetterMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var subscriptionId = request.SubscriptionName ?? request.EntityName;

        var nsResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<IReadOnlyList<Message>>(nsResult.Error);

        var ns = nsResult.Value;

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var mapped = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var (dlqSubscriptionId, _) = await ResolveDlqRouteAsync(ns, subscriptionId, ct).ConfigureAwait(false);
                if (dlqSubscriptionId is null)
                    return new List<Message>();

                var dlqSubscriber = await _clientFactory.GetSubscriberClientAsync(
                    ns, dlqSubscriptionId, ct).ConfigureAwait(false);
                var dlqSubResourceName = GetSubscriptionResourceName(ns, dlqSubscriptionId);
                var messages = await PullAndNackAsync(dlqSubscriber, dlqSubResourceName, request.MaxMessages, ct).ConfigureAwait(false);
                return MapToMessages(messages, request.NamespaceId, request.EntityName, fromDlq: true);
            }, linkedCts.Token).ConfigureAwait(false);

            _logger.LogDebug("Peeked {Count} DLQ messages for Pub/Sub subscription {Subscription}", mapped.Count, SanitizeForLog(subscriptionId));
            return Result.Success<IReadOnlyList<Message>>(mapped);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("GCP Pub/Sub DLQ peek timed out after {Seconds}s for subscription {Subscription}", OperationTimeoutSeconds, SanitizeForLog(subscriptionId));
            return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService(
                "GCP.PubSub.Timeout", $"Pub/Sub DLQ operation timed out after {OperationTimeoutSeconds}s."));
        }
        catch (Grpc.Core.RpcException ex) when (ex.Status.StatusCode == Grpc.Core.StatusCode.FailedPrecondition)
        {
            _logger.LogWarning(ex, "Pub/Sub subscription {Subscription} does not support Pull", SanitizeForLog(subscriptionId));
            return Result.Failure<IReadOnlyList<Message>>(UnsupportedSubscriptionTypeError(subscriptionId));
        }
        catch (Grpc.Core.RpcException ex) when (ex.Status.StatusCode == Grpc.Core.StatusCode.Cancelled)
        {
            // See PeekMessagesAsync — gRPC wraps both our own timeout and a real client
            // disconnect identically, so tell them apart the same way before deciding whether
            // to rethrow as OperationCanceledException (client-disconnect path, 499) or report
            // the timeout (502 GCP.PubSub.Timeout).
            if (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("GCP Pub/Sub DLQ peek timed out after {Seconds}s for subscription {Subscription}", OperationTimeoutSeconds, SanitizeForLog(subscriptionId));
                return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService(
                    "GCP.PubSub.Timeout", $"Pub/Sub DLQ operation timed out after {OperationTimeoutSeconds}s."));
            }

            throw new OperationCanceledException("Pub/Sub DLQ peek cancelled by client.", ex, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error peeking Pub/Sub DLQ messages from {Subscription}", SanitizeForLog(subscriptionId));
            return Result.Failure<IReadOnlyList<Message>>(Error.ExternalService("GCP.PubSub.DlqPeekFailed", ex.Message));
        }
    }

    /// <summary>
    /// Builds the actionable validation error for a subscription whose type doesn't support
    /// Pub/Sub's Pull API. Google returns a bare <c>FailedPrecondition</c> for this — it means
    /// the subscription has a <c>PushConfig</c>, <c>BigqueryConfig</c>, or <c>CloudStorageConfig</c>
    /// (export subscriptions deliver directly to their target and were never pullable), not that
    /// anything is misconfigured. Surfaced as <see cref="ErrorType.Validation"/> (400), not
    /// <see cref="ErrorType.ExternalService"/> (502), since no retry or reconnect fixes it — the
    /// caller must pick a different (Pull-type) subscription.
    /// </summary>
    private static Error UnsupportedSubscriptionTypeError(string subscriptionId) => Error.Validation(
        "GCP.PubSub.UnsupportedSubscriptionType",
        $"Subscription '{subscriptionId}' does not support message browsing. It is a Push, BigQuery, or " +
        "Cloud Storage export subscription — Pub/Sub delivers those directly to their target and never " +
        "makes messages available to Pull/peek. Create a separate Pull subscription on the same topic to " +
        "inspect messages with ServiceHub.");

    // Hard limit per individual Pub/Sub API call. Pub/Sub's synchronous Pull has no working
    // "return immediately" option (the field is deprecated and ignored server-side), so an empty
    // subscription can legitimately take ~20s to come back — confirmed against live Pub/Sub via
    // both this receiver and `gcloud pubsub subscriptions pull`. 15s was cutting those off before
    // they ever had a chance to return, misreporting healthy empty peeks as timeouts on every
    // DlqMonitorWorker cycle. 25s comfortably covers that latency while staying under the SPA's
    // 30s Axios timeout (packages/servicehub-ui-shared/src/lib/api/client.ts).
    private const int OperationTimeoutSeconds = 25;

    /// <inheritdoc/>
    public Task<Result<long>> GetMessageCountAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName = null,
        CancellationToken cancellationToken = default)
    {
        // Pub/Sub has no direct message-count API (counts require the Cloud Monitoring API).
        // Return a neutral success (0) rather than a failure so this read behaves the same
        // shape as the Azure/AWS providers — callers get a uniform, non-error result instead
        // of a provider-specific 400. This mirrors the existing "unsupported read" convention
        // used by GetScheduledMessagesAsync (returns empty).
        _logger.LogDebug(
            "GCP Pub/Sub message count is unavailable via API; returning 0. Subscription: {Subscription}",
            SanitizeForLog(entityName));
        return Task.FromResult(Result.Success(0L));
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Pub/Sub has no native manual dead-letter API (dead-lettering is policy-driven via
    /// MaxDeliveryAttempts). This mirrors the AWS approach for test-driven dead-lettering:
    /// pull messages from the source subscription, republish them to the subscription's
    /// configured dead-letter topic with a <c>DeadLetterReason</c> attribute, then acknowledge
    /// the originals. Requires a dead-letter policy on the subscription — fails with a clear
    /// validation error otherwise, matching the AWS "no DLQ configured" behaviour.
    /// </remarks>
    public async Task<Result<int>> DeadLetterMessagesAsync(
        DeadLetterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nsResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<int>(nsResult.Error);

        var ns = nsResult.Value;

        var subscriptionId = request.SubscriptionName ?? request.EntityName;

        try
        {
            var subscriber = await _clientFactory.GetSubscriberClientAsync(
                ns, subscriptionId, cancellationToken).ConfigureAwait(false);
            var subResourceName = GetSubscriptionResourceName(ns, subscriptionId);

            // Resolve the dead-letter topic from the subscription's policy — the authoritative
            // Pub/Sub source for where dead-lettered messages belong.
            var subscription = await subscriber.GetSubscriptionAsync(new GetSubscriptionRequest
            {
                Subscription = subResourceName
            }, cancellationToken).ConfigureAwait(false);

            var deadLetterTopic = subscription.DeadLetterPolicy?.DeadLetterTopic;
            if (string.IsNullOrEmpty(deadLetterTopic))
            {
                return Result.Failure<int>(Error.Validation("GCP.PubSub.NoDlq",
                    $"Subscription {subscriptionId} has no dead-letter policy configured. " +
                    "Set a dead-letter topic and MaxDeliveryAttempts on the subscription."));
            }

            var deadLetterTopicId = TopicName.Parse(deadLetterTopic).TopicId;
            var publisher = await _clientFactory.GetPublisherClientAsync(
                ns, deadLetterTopicId, cancellationToken).ConfigureAwait(false);

            // Pull with a real ack window (no nack) — these messages are being removed.
            var pull = await subscriber.PullAsync(new PullRequest
            {
                Subscription = subResourceName,
                MaxMessages = request.ValidatedMessageCount
            }, cancellationToken).ConfigureAwait(false);

            if (pull.ReceivedMessages.Count == 0)
                return Result.Success(0);

            var ackIds = new List<string>(pull.ReceivedMessages.Count);
            foreach (var received in pull.ReceivedMessages)
            {
                var forwarded = new PubsubMessage { Data = received.Message.Data };
                foreach (var attribute in received.Message.Attributes)
                    forwarded.Attributes[attribute.Key] = attribute.Value;
                forwarded.Attributes["DeadLetterReason"] = request.Reason;
                if (!string.IsNullOrEmpty(request.ErrorDescription))
                    forwarded.Attributes["DeadLetterErrorDescription"] = request.ErrorDescription;

                await publisher.PublishAsync(forwarded).ConfigureAwait(false);
                ackIds.Add(received.AckId);
            }

            // Acknowledge the originals — removes them from the source subscription.
            await subscriber.AcknowledgeAsync(new AcknowledgeRequest
            {
                Subscription = subResourceName,
                AckIds = { ackIds }
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Dead-lettered {Count} messages from subscription {Subscription} to topic {Topic}",
                ackIds.Count, SanitizeForLog(subscriptionId), SanitizeForLog(deadLetterTopicId));
            return Result.Success(ackIds.Count);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error dead-lettering Pub/Sub messages from {Subscription}", SanitizeForLog(subscriptionId));
            return Result.Failure<int>(Error.ExternalService("GCP.PubSub.DlqFailed", ex.Message));
        }
    }

    private const string RecoveryMarkerAttribute = "x-servicehub-recovery-id";

    /// <inheritdoc/>
    public async Task<Result<bool>> ReplayMessageAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        string? recoveryMarker,
        CancellationToken cancellationToken = default)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result<bool>.Failure(nsResult.Error);

        var baseSubscriptionId = subscriptionName ?? entityName;

        try
        {
            // Replay always moves a message from the DLQ back to its source topic (mirrors the
            // AWS provider, which always resolves and scans the DLQ queue here too). Pub/Sub has
            // no cross-subscription "move", so this publishes a fresh copy of the scanned payload
            // to the source topic, then permanently acknowledges the DLQ copy.
            var (dlqSubscriptionId, sourceTopicId) = await ResolveDlqRouteAsync(
                nsResult.Value, baseSubscriptionId, cancellationToken).ConfigureAwait(false);
            if (dlqSubscriptionId is null || sourceTopicId is null)
            {
                return Result<bool>.Failure(Error.Validation("GCP.PubSub.NoDlq",
                    $"Subscription {baseSubscriptionId} has no dead-letter subscription to replay from."));
            }

            var dlqSubscriber = await _clientFactory.GetSubscriberClientAsync(
                nsResult.Value, dlqSubscriptionId, cancellationToken).ConfigureAwait(false);
            var dlqSubResourceName = GetSubscriptionResourceName(nsResult.Value, dlqSubscriptionId);

            var target = await FindAndLockMessageAsync(dlqSubscriber, dlqSubResourceName, sequenceNumber, cancellationToken)
                .ConfigureAwait(false);
            if (target is null)
            {
                return Result<bool>.Failure(Error.NotFound("GCP.PubSub.MessageNotFound",
                    $"Message {sequenceNumber} not found in DLQ subscription {dlqSubscriptionId}."));
            }

            var publisher = await _clientFactory.GetPublisherClientAsync(
                nsResult.Value, sourceTopicId, cancellationToken).ConfigureAwait(false);
            var republished = new PubsubMessage { Data = target.Message.Data };
            foreach (var attribute in target.Message.Attributes)
                republished.Attributes[attribute.Key] = attribute.Value;

            // Recovery Evidence Ledger marker — the one behavioural change replay makes for
            // verification (see RecoveryLedgerEntry.RecoveryMarker). Pub/Sub allows up to 100
            // attributes per message, far above what a dead-lettered message would realistically
            // carry, so this is unconditional whenever a marker is supplied.
            var markerApplied = false;
            if (!string.IsNullOrEmpty(recoveryMarker))
            {
                republished.Attributes[RecoveryMarkerAttribute] = recoveryMarker;
                markerApplied = true;
            }

            await publisher.PublishAsync(republished).ConfigureAwait(false);

            // Acknowledge = permanently remove the DLQ copy now that a fresh one has been published.
            await dlqSubscriber.AcknowledgeAsync(new AcknowledgeRequest
            {
                Subscription = dlqSubResourceName,
                AckIds = { target.AckId }
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Replayed Pub/Sub message {Seq} from {DlqSubscription} to topic {Topic}",
                sequenceNumber, SanitizeForLog(dlqSubscriptionId), SanitizeForLog(sourceTopicId));
            return Result<bool>.Success(markerApplied);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error replaying Pub/Sub message {Seq} for {Subscription}", sequenceNumber, SanitizeForLog(baseSubscriptionId));
            return Result<bool>.Failure(Error.ExternalService("GCP.PubSub.ReplayFailed", ex.Message));
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
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure(nsResult.Error);

        var baseSubscriptionId = subscriptionName ?? entityName;

        try
        {
            string subscriptionId;
            if (fromDeadLetter)
            {
                var (dlqSubscriptionId, _) = await ResolveDlqRouteAsync(
                    nsResult.Value, baseSubscriptionId, cancellationToken).ConfigureAwait(false);
                if (dlqSubscriptionId is null)
                {
                    return Result.Failure(Error.Validation("GCP.PubSub.NoDlq",
                        $"Subscription {baseSubscriptionId} has no dead-letter subscription to purge from."));
                }
                subscriptionId = dlqSubscriptionId;
            }
            else
            {
                subscriptionId = baseSubscriptionId;
            }

            var subscriber = await _clientFactory.GetSubscriberClientAsync(
                nsResult.Value, subscriptionId, cancellationToken).ConfigureAwait(false);
            var subResourceName = GetSubscriptionResourceName(nsResult.Value, subscriptionId);

            var target = await FindAndLockMessageAsync(subscriber, subResourceName, sequenceNumber, cancellationToken)
                .ConfigureAwait(false);
            if (target is null)
            {
                return Result.Failure(Error.NotFound("GCP.PubSub.MessageNotFound",
                    $"Message {sequenceNumber} not found in subscription {subscriptionId}."));
            }

            // Acknowledge = permanently delete from subscription
            await subscriber.AcknowledgeAsync(new AcknowledgeRequest
            {
                Subscription = subResourceName,
                AckIds = { target.AckId }
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Purged Pub/Sub message {Seq} from {Subscription}", sequenceNumber, SanitizeForLog(subscriptionId));
            return Result.Success();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error purging Pub/Sub message {Seq} from {Subscription}", sequenceNumber, SanitizeForLog(baseSubscriptionId));
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
            SanitizeForLog(entityName));
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
            _logger.LogError(ex, "Error getting ack deadline status for {Subscription}", SanitizeForLog(subscriptionId));
            return Result.Failure<GcpAckDeadlineStatus>(Error.ExternalService("GCP.PubSub.AckStatusFailed", ex.Message));
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────
    private static string SanitizeForLog(string? value)
        => (value ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
    /// <summary>
    /// Pulls up to <paramref name="maxMessages"/> distinct messages, immediately nacking each
    /// batch to keep the read non-destructive. A single Pull call doesn't reliably return
    /// everything currently available — especially under concurrent contention from other
    /// pullers on the same subscription, like <c>DlqMonitorWorker</c>'s own background scan —
    /// so this loops (deduping by MessageId) until a quiet round or <see cref="MaxScanBatches"/>
    /// is hit, mirroring the AWS provider's equivalent peek-completeness fix for SQS.
    /// </summary>
    private async Task<List<ReceivedMessage>> PullAndNackAsync(
        SubscriberServiceApiClient subscriber, string subscriptionResourceName, int maxMessages, CancellationToken ct)
    {
        var messages = new List<ReceivedMessage>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        for (var i = 0; i < MaxScanBatches && messages.Count < maxMessages; i++)
        {
            var response = await subscriber.PullAsync(new PullRequest
            {
                Subscription = subscriptionResourceName,
                MaxMessages = maxMessages - messages.Count
            }, ct).ConfigureAwait(false);

            if (response.ReceivedMessages.Count == 0)
                break;

            var newlySeen = response.ReceivedMessages.Where(m => seenIds.Add(m.Message.MessageId)).ToList();
            messages.AddRange(newlySeen);

            if (newlySeen.Count > 0)
            {
                // ModifyAckDeadline(0) = immediately re-queue (peek pattern)
                await subscriber.ModifyAckDeadlineAsync(new ModifyAckDeadlineRequest
                {
                    Subscription = subscriptionResourceName,
                    AckIds = { newlySeen.Select(m => m.AckId) },
                    AckDeadlineSeconds = 0
                }, ct).ConfigureAwait(false);
            }
            else
            {
                break; // quiet round: everything in this batch was a dup we've already released
            }
        }

        return messages;
    }

    /// <summary>
    /// Scans a subscription for the message whose MessageId hashes to <paramref name="sequenceNumber"/>,
    /// extending the ack deadline on everything received during the scan so a concurrent puller
    /// (e.g. <c>DlqMonitorWorker</c>'s own background scan) can't steal it mid-search. The target
    /// (if found) is returned with a fresh, currently-valid ack ID; every other message seen is
    /// released (nacked) back for immediate redelivery to other viewers.
    /// </summary>
    private async Task<ReceivedMessage?> FindAndLockMessageAsync(
        SubscriberServiceApiClient subscriber, string subscriptionResourceName, long sequenceNumber, CancellationToken ct)
    {
        ReceivedMessage? target = null;
        var nonTargets = new List<ReceivedMessage>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        var gate = GetScanGate(subscriptionResourceName);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var i = 0; i < MaxScanBatches && target is null; i++)
            {
                var response = await subscriber.PullAsync(new PullRequest
                {
                    Subscription = subscriptionResourceName,
                    MaxMessages = ScanBatchSize
                }, ct).ConfigureAwait(false);

                if (response.ReceivedMessages.Count == 0)
                    break;

                var progressed = false;
                var newlySeen = new List<ReceivedMessage>();
                foreach (var received in response.ReceivedMessages)
                {
                    if (!seenIds.Add(received.Message.MessageId))
                        continue;

                    progressed = true;
                    newlySeen.Add(received);
                    if (ComputeSequenceNumber(received.Message.MessageId) == sequenceNumber)
                        target = received;
                    else
                        nonTargets.Add(received);
                }

                if (newlySeen.Count > 0)
                {
                    // Hold everything we've seen so far behind a real lock for the rest of the
                    // scan — otherwise it becomes redeliverable to another puller before we've
                    // decided whether it's the target.
                    await subscriber.ModifyAckDeadlineAsync(new ModifyAckDeadlineRequest
                    {
                        Subscription = subscriptionResourceName,
                        AckIds = { newlySeen.Select(m => m.AckId) },
                        AckDeadlineSeconds = ScanLockSeconds
                    }, ct).ConfigureAwait(false);
                }

                if (!progressed)
                    break;
            }
        }
        finally
        {
            try
            {
                if (nonTargets.Count > 0)
                {
                    try
                    {
                        await subscriber.ModifyAckDeadlineAsync(new ModifyAckDeadlineRequest
                        {
                            Subscription = subscriptionResourceName,
                            AckIds = { nonTargets.Select(m => m.AckId) },
                            AckDeadlineSeconds = 0
                        }, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "Failed to release scanned Pub/Sub messages; they reappear after the {Seconds}s lock expires",
                            ScanLockSeconds);
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }

        return target;
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
            var seqNum = ComputeSequenceNumber(msg.MessageId);

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
                // Pub/Sub has no dedicated correlation field — pulled from message attributes
                // so DlqMonitorService can persist it and Cross-Cloud Trace's historical-DLQ
                // lookup can find this message after it's no longer peekable live.
                CorrelationId = ServiceHub.Shared.Helpers.MessageCorrelationIdExtractor.Extract(appProps),
                NamespaceId = namespaceId,
                EntityName = entityName,
                IsFromDeadLetter = fromDlq,
                State = MessageState.Active
            });
        }

        return mapped;
    }

    private static long ComputeSequenceNumber(string messageId)
    {
        // Hash the stable MessageId, not the single-delivery ack ID (which rotates on every
        // pull) — the same sequence number must still resolve the message after the ack ID a
        // user's peek saw has been superseded by a later pull (e.g. DlqMonitorWorker's own scan).
        // SHA-256 keeps this consistent across process restarts (unlike GetHashCode, which is
        // randomized) with negligible collision probability for realistic queue depths. Masked to
        // 53 bits so every value stays within Number.MAX_SAFE_INTEGER (9007199254740991) — the
        // full 63-bit range silently corrupts in JS's double-precision Number, breaking
        // replay/purge lookups on the JSON round-trip through the browser.
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(messageId));
        return BitConverter.ToInt64(hash, 0) & ((1L << 53) - 1);
    }

    private static string GetSubscriptionResourceName(Core.Entities.Namespace ns, string subscriptionId)
    {
        var projectId = ns.GcpProjectId ?? "unknown-project";
        return SubscriptionName.FromProjectSubscription(projectId, subscriptionId).ToString();
    }

    /// <summary>
    /// Resolves the subscription that reads from a source subscription's configured
    /// dead-letter topic, plus the source subscription's own topic (where a replayed message
    /// must be re-published — Pub/Sub has no cross-subscription "move"). There is no fixed
    /// naming convention for the DLQ subscription, so it reads the source subscription's
    /// <c>DeadLetterPolicy</c> to find the DLQ topic, then returns the first subscription
    /// attached to that topic. <c>DlqSubscriptionId</c> is <see langword="null"/> if the source
    /// subscription has no dead-letter policy or its DLQ topic has no subscription.
    /// </summary>
    private async Task<(string? DlqSubscriptionId, string? SourceTopicId)> ResolveDlqRouteAsync(
        Core.Entities.Namespace ns, string subscriptionId, CancellationToken ct)
    {
        var subscriber = await _clientFactory.GetSubscriberClientAsync(ns, subscriptionId, ct).ConfigureAwait(false);
        var subResourceName = GetSubscriptionResourceName(ns, subscriptionId);
        var subscription = await subscriber.GetSubscriptionAsync(new GetSubscriptionRequest
        {
            Subscription = subResourceName
        }, ct).ConfigureAwait(false);

        var sourceTopicId = subscription.TopicAsTopicName?.TopicId;

        var deadLetterTopic = subscription.DeadLetterPolicy?.DeadLetterTopic;
        if (string.IsNullOrEmpty(deadLetterTopic))
            return (null, sourceTopicId);

        var topicAdmin = await _clientFactory.GetTopicAdminClientAsync(ns, ct).ConfigureAwait(false);
        await foreach (var subName in topicAdmin
            .ListTopicSubscriptionsAsync(TopicName.Parse(deadLetterTopic))
            .WithCancellation(ct))
        {
            return (SubscriptionName.Parse(subName).SubscriptionId, sourceTopicId);
        }

        return (null, sourceTopicId);
    }
}
