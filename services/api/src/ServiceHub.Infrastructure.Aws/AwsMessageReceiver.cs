using System.Collections.Concurrent;
using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Aws.Models;
using ServiceHub.Shared.Results;
using CoreMessage = ServiceHub.Core.Entities.Message;
using SqsMessage = Amazon.SQS.Model.Message;
using SqsSend = Amazon.SQS.Model.SendMessageRequest;

namespace ServiceHub.Infrastructure.Aws;

/// <summary>
/// Implements <see cref="IMessageReceiver"/> for Amazon SQS queues.
/// <para>
/// Peek behaviour: SQS has no native non-destructive read. This implementation uses
/// <c>VisibilityTimeout=0</c> — messages are received but immediately become visible
/// again, so no consumer is blocked and no message is removed.
/// </para>
/// </summary>
public sealed class AwsMessageReceiver : IMessageReceiver, IVisibilityStatusProvider
{
    private readonly IAwsClientFactory _clientFactory;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<AwsMessageReceiver> _logger;

    // Maps synthetic sequence number → SQS ReceiptHandle so replay/purge can find the message.
    // Receipt handles are large opaque strings; we expose a stable long ID to the UI layer.
    private readonly ConcurrentDictionary<long, string> _receiptHandleCache = new();

    // Caches DLQ URL per (namespaceId, sourceQueueUrl) to avoid repeated GetQueueAttributes calls.
    private readonly ConcurrentDictionary<string, string> _dlqUrlCache = new();

    /// <summary>SQS hard limit for ReceiveMessage batch size.</summary>
    private const int SqsMaxBatchSize = 10;

    /// <summary>
    /// Initialises a new instance of <see cref="AwsMessageReceiver"/>.
    /// </summary>
    /// <param name="clientFactory">Factory that creates IAmazonSQS clients per namespace.</param>
    /// <param name="namespaceRepository">Repository for resolving namespace credentials by ID.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AwsMessageReceiver(
        IAwsClientFactory clientFactory,
        INamespaceRepository namespaceRepository,
        ILogger<AwsMessageReceiver> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    // ── IMessageReceiver ──────────────────────────────────────────────────────

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<CoreMessage>>> PeekMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nsResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<IReadOnlyList<CoreMessage>>(nsResult.Error);

        var ns = nsResult.Value;
        var sqs = _clientFactory.GetSqsClient(ns);

        try
        {
            var queueUrl = await ResolveQueueUrlAsync(sqs, request.EntityName, cancellationToken).ConfigureAwait(false);
            var messages = await PeekFromUrlAsync(sqs, queueUrl, request.MaxMessages, cancellationToken).ConfigureAwait(false);
            var mapped = MapToMessages(messages, request.NamespaceId, request.EntityName, fromDlq: false);

            _logger.LogDebug("Peeked {Count} messages from SQS queue {QueueName} (namespace {NamespaceId})",
                mapped.Count, request.EntityName, request.NamespaceId);
            return Result.Success<IReadOnlyList<CoreMessage>>(mapped);
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error peeking messages from {QueueName}", request.EntityName);
            return Result.Failure<IReadOnlyList<CoreMessage>>(Error.ExternalService(
                "AWS.SQS.PeekFailed", $"SQS error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error peeking SQS messages from {QueueName}", request.EntityName);
            return Result.Failure<IReadOnlyList<CoreMessage>>(Error.Internal("AWS.SQS.UnexpectedError", ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<CoreMessage>>> PeekDeadLetterMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var nsResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<IReadOnlyList<CoreMessage>>(nsResult.Error);

        var ns = nsResult.Value;
        var sqs = _clientFactory.GetSqsClient(ns);

        try
        {
            var sourceUrl = await ResolveQueueUrlAsync(sqs, request.EntityName, cancellationToken).ConfigureAwait(false);
            var dlqUrl = await ResolveDlqUrlAsync(sqs, sourceUrl, cancellationToken).ConfigureAwait(false);

            if (dlqUrl is null)
            {
                _logger.LogWarning("Queue {QueueName} has no DLQ configured (no RedrivePolicy)", request.EntityName);
                return Result.Success<IReadOnlyList<CoreMessage>>(Array.Empty<CoreMessage>());
            }

            var messages = await PeekFromUrlAsync(sqs, dlqUrl, request.MaxMessages, cancellationToken).ConfigureAwait(false);
            var mapped = MapToMessages(messages, request.NamespaceId, request.EntityName, fromDlq: true);

            _logger.LogDebug("Peeked {Count} DLQ messages from {QueueName} (namespace {NamespaceId})",
                mapped.Count, request.EntityName, request.NamespaceId);
            return Result.Success<IReadOnlyList<CoreMessage>>(mapped);
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error peeking DLQ messages from {QueueName}", request.EntityName);
            return Result.Failure<IReadOnlyList<CoreMessage>>(Error.ExternalService(
                "AWS.SQS.DlqPeekFailed", $"SQS error: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error peeking SQS DLQ messages from {QueueName}", request.EntityName);
            return Result.Failure<IReadOnlyList<CoreMessage>>(Error.Internal("AWS.SQS.UnexpectedError", ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<long>> GetMessageCountAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName = null,
        CancellationToken cancellationToken = default)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<long>(nsResult.Error);

        var sqs = _clientFactory.GetSqsClient(nsResult.Value);

        try
        {
            var queueUrl = await ResolveQueueUrlAsync(sqs, entityName, cancellationToken).ConfigureAwait(false);
            var attrs = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                AttributeNames = new List<string> { "ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible" }
            }, cancellationToken).ConfigureAwait(false);

            var visible = (long)attrs.ApproximateNumberOfMessages;
            var inFlight = (long)attrs.ApproximateNumberOfMessagesNotVisible;
            return Result.Success(visible + inFlight);
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error getting message count for {QueueName}", entityName);
            return Result.Failure<long>(Error.ExternalService("AWS.SQS.CountFailed", ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<int>> DeadLetterMessagesAsync(
        DeadLetterRequest request,
        CancellationToken cancellationToken = default)
    {
        // For AWS: send a message with a custom DeadLetterReason attribute to the DLQ URL.
        var nsResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<int>(nsResult.Error);

        var sqs = _clientFactory.GetSqsClient(nsResult.Value);

        try
        {
            var sourceUrl = await ResolveQueueUrlAsync(sqs, request.EntityName, cancellationToken).ConfigureAwait(false);
            var dlqUrl = await ResolveDlqUrlAsync(sqs, sourceUrl, cancellationToken).ConfigureAwait(false);
            if (dlqUrl is null)
                return Result.Failure<int>(Error.Validation("AWS.SQS.NoDlq",
                    $"Queue {request.EntityName} has no DLQ configured."));

            // Receive messages from source and delete them (they're now in the DLQ by policy-driven path)
            // For manual dead-lettering, we receive them and send to DLQ directly.
            var count = Math.Min(request.ValidatedMessageCount, SqsMaxBatchSize);
            var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = sourceUrl,
                MaxNumberOfMessages = count,
                VisibilityTimeout = 30,
                MessageSystemAttributeNames = new List<string> { "All" },
                MessageAttributeNames = new List<string> { "All" }
            }, cancellationToken).ConfigureAwait(false);

            if (received.Messages.Count == 0)
                return Result.Success(0);

            // Send each to DLQ with reason attribute, then delete from source
            var deadLettered = 0;
            foreach (var msg in received.Messages)
            {
                await sqs.SendMessageAsync(new SqsSend
                {
                    QueueUrl = dlqUrl,
                    MessageBody = msg.Body,
                    MessageAttributes = new Dictionary<string, MessageAttributeValue>
                    {
                        ["DeadLetterReason"] = new MessageAttributeValue { DataType = "String", StringValue = request.Reason }
                    }
                }, cancellationToken).ConfigureAwait(false);

                await sqs.DeleteMessageAsync(new DeleteMessageRequest
                {
                    QueueUrl = sourceUrl,
                    ReceiptHandle = msg.ReceiptHandle
                }, cancellationToken).ConfigureAwait(false);

                deadLettered++;
            }

            _logger.LogInformation("Dead-lettered {Count} messages from {QueueName}", deadLettered, request.EntityName);
            return Result.Success(deadLettered);
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error dead-lettering messages from {QueueName}", request.EntityName);
            return Result.Failure<int>(Error.ExternalService("AWS.SQS.DlqFailed", ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> ReplayMessageAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        long sequenceNumber,
        CancellationToken cancellationToken = default)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure(nsResult.Error);

        var sqs = _clientFactory.GetSqsClient(nsResult.Value);

        try
        {
            var sourceUrl = await ResolveQueueUrlAsync(sqs, entityName, cancellationToken).ConfigureAwait(false);
            var dlqUrl = await ResolveDlqUrlAsync(sqs, sourceUrl, cancellationToken).ConfigureAwait(false);

            if (dlqUrl is null)
                return Result.Failure(Error.Validation("AWS.SQS.NoDlq",
                    $"Queue {entityName} has no DLQ configured."));

            // Receive the target message from DLQ with a real visibility window
            if (!_receiptHandleCache.TryGetValue(sequenceNumber, out var cachedHandle))
            {
                // Receipt handle not in cache — scan for the message
                _logger.LogWarning("Receipt handle for sequence {Seq} not cached, scanning DLQ for {QueueName}", sequenceNumber, entityName);
                return Result.Failure(Error.NotFound("AWS.SQS.MessageNotFound",
                    $"Message with sequence number {sequenceNumber} not found in receipt handle cache. " +
                    "Peek the DLQ first to populate the cache before replaying."));
            }

            // Receive from DLQ with proper visibility timeout to lock the message
            var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = dlqUrl,
                MaxNumberOfMessages = 1,
                VisibilityTimeout = 30,
                MessageSystemAttributeNames = new List<string> { "All" },
                MessageAttributeNames = new List<string> { "All" }
            }, cancellationToken).ConfigureAwait(false);

            var target = received.Messages.FirstOrDefault(m =>
                string.Equals(m.ReceiptHandle, cachedHandle, StringComparison.Ordinal));

            if (target is null)
            {
                _logger.LogWarning("Message with sequence {Seq} not found in DLQ receive batch", sequenceNumber);
                return Result.Failure(Error.NotFound("AWS.SQS.MessageNotFound",
                    $"Message {sequenceNumber} could not be received from the DLQ."));
            }

            // CRITICAL ORDER: Send to source BEFORE deleting from DLQ
            await sqs.SendMessageAsync(new SqsSend
            {
                QueueUrl = sourceUrl,
                MessageBody = target.Body,
                MessageAttributes = target.MessageAttributes
            }, cancellationToken).ConfigureAwait(false);

            await sqs.DeleteMessageAsync(new DeleteMessageRequest
            {
                QueueUrl = dlqUrl,
                ReceiptHandle = target.ReceiptHandle
            }, cancellationToken).ConfigureAwait(false);

            _receiptHandleCache.TryRemove(sequenceNumber, out _);
            _logger.LogInformation("Replayed message {Seq} from DLQ to {QueueName}", sequenceNumber, entityName);
            return Result.Success();
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error replaying message {Seq} for {QueueName}", sequenceNumber, entityName);
            return Result.Failure(Error.ExternalService("AWS.SQS.ReplayFailed", ex.Message));
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

        var sqs = _clientFactory.GetSqsClient(nsResult.Value);

        try
        {
            var sourceUrl = await ResolveQueueUrlAsync(sqs, entityName, cancellationToken).ConfigureAwait(false);
            string targetUrl;

            if (fromDeadLetter)
            {
                var dlqUrl = await ResolveDlqUrlAsync(sqs, sourceUrl, cancellationToken).ConfigureAwait(false);
                if (dlqUrl is null)
                    return Result.Failure(Error.Validation("AWS.SQS.NoDlq", $"Queue {entityName} has no DLQ."));
                targetUrl = dlqUrl;
            }
            else
            {
                targetUrl = sourceUrl;
            }

            if (!_receiptHandleCache.TryGetValue(sequenceNumber, out var receiptHandle))
            {
                return Result.Failure(Error.NotFound("AWS.SQS.MessageNotFound",
                    $"Message {sequenceNumber} not found in receipt handle cache."));
            }

            await sqs.DeleteMessageAsync(new DeleteMessageRequest
            {
                QueueUrl = targetUrl,
                ReceiptHandle = receiptHandle
            }, cancellationToken).ConfigureAwait(false);

            _receiptHandleCache.TryRemove(sequenceNumber, out _);
            _logger.LogInformation("Purged message {Seq} from {Queue}", sequenceNumber, entityName);
            return Result.Success();
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error purging message {Seq} for {QueueName}", sequenceNumber, entityName);
            return Result.Failure(Error.ExternalService("AWS.SQS.PurgeFailed", ex.Message));
        }
    }

    /// <inheritdoc/>
    public Task<Result<IReadOnlyList<CoreMessage>>> GetScheduledMessagesAsync(
        Guid namespaceId,
        string entityName,
        string? subscriptionName,
        int maxMessages,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "AWS SQS does not support scheduled message inspection. " +
            "Use EventBridge Scheduler for scheduled delivery. Queue: {QueueName}",
            entityName);

        return Task.FromResult(
            Result.Success<IReadOnlyList<CoreMessage>>(Array.Empty<CoreMessage>()));
    }

    // ── AWS-specific public features ──────────────────────────────────────────

    /// <summary>
    /// Returns in-flight and DLQ counts for a queue, surfacing the most common SQS
    /// pain point: messages that are "invisible" while within their visibility timeout.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="queueName">The queue name (not URL).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A <see cref="SqsVisibilityInfo"/> snapshot for the queue.</returns>
    public async Task<Result<SqsVisibilityInfo>> GetVisibilityWindowStatusAsync(
        Guid namespaceId,
        string queueName,
        CancellationToken cancellationToken = default)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<SqsVisibilityInfo>(nsResult.Error);

        var sqs = _clientFactory.GetSqsClient(nsResult.Value);

        try
        {
            var queueUrl = await ResolveQueueUrlAsync(sqs, queueName, cancellationToken).ConfigureAwait(false);
            var attrs = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                AttributeNames = new List<string>
                {
                    "ApproximateNumberOfMessagesNotVisible",
                    "VisibilityTimeout",
                    "RedrivePolicy"
                }
            }, cancellationToken).ConfigureAwait(false);

            var inFlight = attrs.ApproximateNumberOfMessagesNotVisible;
            var vt = attrs.VisibilityTimeout;

            // Get DLQ count if RedrivePolicy is configured
            var dlqCount = 0;
            if (attrs.Attributes.TryGetValue("RedrivePolicy", out var redrive) && !string.IsNullOrEmpty(redrive))
            {
                try
                {
                    var dlqUrl = await ResolveDlqUrlAsync(sqs, queueUrl, cancellationToken).ConfigureAwait(false);
                    if (dlqUrl is not null)
                    {
                        var dlqAttrs = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
                        {
                            QueueUrl = dlqUrl,
                            AttributeNames = new List<string> { "ApproximateNumberOfMessages" }
                        }, cancellationToken).ConfigureAwait(false);
                        dlqCount = dlqAttrs.ApproximateNumberOfMessages;
                    }
                }
                catch (AmazonSQSException)
                {
                    // Best-effort DLQ count
                }
            }

            return Result.Success(new SqsVisibilityInfo(inFlight, vt, dlqCount));
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error getting visibility status for {QueueName}", queueName);
            return Result.Failure<SqsVisibilityInfo>(Error.ExternalService("AWS.SQS.VisibilityFailed", ex.Message));
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<string> ResolveQueueUrlAsync(
        IAmazonSQS sqs, string queueName, CancellationToken ct)
    {
        // If it already looks like a full URL, use it directly.
        if (queueName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return queueName;

        var response = await sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = queueName }, ct)
            .ConfigureAwait(false);
        return response.QueueUrl;
    }

    private async Task<string?> ResolveDlqUrlAsync(IAmazonSQS sqs, string sourceQueueUrl, CancellationToken ct)
    {
        if (_dlqUrlCache.TryGetValue(sourceQueueUrl, out var cached))
            return cached;

        var attrs = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
        {
            QueueUrl = sourceQueueUrl,
            AttributeNames = new List<string> { "RedrivePolicy" }
        }, ct).ConfigureAwait(false);

        if (!attrs.Attributes.TryGetValue("RedrivePolicy", out var redriveJson) || string.IsNullOrEmpty(redriveJson))
            return null;

        // Parse {"maxReceiveCount":N,"deadLetterTargetArn":"arn:aws:sqs:..."}
        using var doc = JsonDocument.Parse(redriveJson);
        if (!doc.RootElement.TryGetProperty("deadLetterTargetArn", out var arnElem))
            return null;

        var dlqArn = arnElem.GetString();
        if (string.IsNullOrEmpty(dlqArn))
            return null;

        // Extract queue name from ARN: arn:aws:sqs:region:account:queue-name
        var queueName = dlqArn.Split(':').LastOrDefault();
        if (string.IsNullOrEmpty(queueName))
            return null;

        var urlResponse = await sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = queueName }, ct)
            .ConfigureAwait(false);
        var dlqUrl = urlResponse.QueueUrl;
        _dlqUrlCache.TryAdd(sourceQueueUrl, dlqUrl);
        return dlqUrl;
    }

    private async Task<List<SqsMessage>> PeekFromUrlAsync(
        IAmazonSQS sqs, string queueUrl, int maxMessages, CancellationToken ct)
    {
        var allMessages = new List<SqsMessage>();
        var remaining = maxMessages;

        while (remaining > 0)
        {
            var batch = Math.Min(remaining, SqsMaxBatchSize);
            var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
            {
                QueueUrl = queueUrl,
                MaxNumberOfMessages = batch,
                VisibilityTimeout = 0,      // ← peek: immediately re-visible
                MessageSystemAttributeNames = new List<string> { "All" },
                MessageAttributeNames = new List<string> { "All" }
            }, ct).ConfigureAwait(false);

            if (response.Messages.Count == 0)
                break;

            allMessages.AddRange(response.Messages);
            remaining -= response.Messages.Count;

            // SQS may return fewer than requested even when messages exist.
            // Stop looping if we got a full batch to avoid infinite polling.
            if (response.Messages.Count < batch)
                break;
        }

        return allMessages;
    }

    private List<CoreMessage> MapToMessages(
        IReadOnlyList<SqsMessage> sqsMessages,
        Guid namespaceId,
        string entityName,
        bool fromDlq)
    {
        var mapped = new List<CoreMessage>(sqsMessages.Count);

        foreach (var msg in sqsMessages)
        {
            // Derive a stable long ID from the receipt handle hash for the UI sequence number.
            var seqNum = ComputeSequenceNumber(msg.ReceiptHandle);
            _receiptHandleCache.TryAdd(seqNum, msg.ReceiptHandle);

            // Parse system attributes
            _ = long.TryParse(
                msg.Attributes.GetValueOrDefault("SentTimestamp", "0"),
                out var sentEpochMs);

            _ = int.TryParse(
                msg.Attributes.GetValueOrDefault("ApproximateReceiveCount", "1"),
                out var deliveryCount);

            var enqueuedTime = sentEpochMs > 0
                ? DateTimeOffset.FromUnixTimeMilliseconds(sentEpochMs)
                : DateTimeOffset.UtcNow;

            // Map MessageAttributes → ApplicationProperties
            var appProps = msg.MessageAttributes.Count > 0
                ? msg.MessageAttributes.ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object)kvp.Value.StringValue)
                : null;

            mapped.Add(new CoreMessage
            {
                MessageId = msg.MessageId,
                SequenceNumber = seqNum,
                Body = msg.Body,
                DeliveryCount = deliveryCount,
                EnqueuedTime = enqueuedTime,
                ApplicationProperties = appProps is { Count: > 0 }
                    ? appProps as IReadOnlyDictionary<string, object>
                    : null,
                NamespaceId = namespaceId,
                EntityName = entityName,
                IsFromDeadLetter = fromDlq,
                State = ServiceHub.Core.Enums.MessageState.Active
            });
        }

        return mapped;
    }

    private static long ComputeSequenceNumber(string receiptHandle)
    {
        // Use a stable hash of the receipt handle as the synthetic sequence number.
        // The receipt handle is unique per receive call, so this is a reasonable proxy.
        return Math.Abs((long)receiptHandle.GetHashCode());
    }
}
