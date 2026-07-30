using System.Collections.Concurrent;
using System.Text.Json;
using Amazon.Runtime;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Aws.Models;
using ServiceHub.Infrastructure.Aws.Resilience;
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
    private readonly ResiliencePipeline _resiliencePipeline;

    // Caches DLQ URL per (namespaceId, sourceQueueUrl) to avoid repeated GetQueueAttributes calls.
    private readonly ConcurrentDictionary<string, string> _dlqUrlCache = new();

    /// <summary>Maximum DLQ URL cache entries (one per distinct source queue).</summary>
    private const int DlqUrlCacheMaxSize = 1_000;

    /// <summary>SQS hard limit for ReceiveMessage batch size.</summary>
    private const int SqsMaxBatchSize = 10;

    /// <summary>Visibility lock (seconds) applied while scanning a queue for a target message.</summary>
    private const int ScanLockSeconds = 60;

    // Scans hold visibility locks while iterating, so two concurrent scans of the
    // same queue (a UI peek racing the DLQ monitor's peek, or a replay/purge scan)
    // would hide messages from each other and report them missing. Serialize
    // receive-scans per queue URL within this process; entries are one semaphore
    // per distinct queue URL, so growth is bounded by the number of queues.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _queueScanGates = new();

    private static SemaphoreSlim GetScanGate(string queueUrl) =>
        _queueScanGates.GetOrAdd(queueUrl, static _ => new SemaphoreSlim(1, 1));

    private const int MaxMessageAttributes = 10;

    private const string OverflowAttributeName = "shs-overflow-properties";

    private const string DeadLetterReasonAttribute = "DeadLetterReason";

    private const string DeadLetterDescriptionAttribute = "DeadLetterErrorDescription";

    /// <summary>Upper bound of receive batches when scanning for a target message.</summary>
    private const int MaxScanBatches = 20;

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
        _resiliencePipeline = AwsResiliencePipeline.Create(_logger);
    }

    // ── IMessageReceiver ──────────────────────────────────────────────────────

    // Hard limit per individual SQS API call so a slow queue cannot stall the UI.
    private const int OperationTimeoutSeconds = 15;

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<CoreMessage>>> PeekMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // An SNS "subscription" is an SQS endpoint queue named after the subscription;
        // topic-scoped requests must target that queue, not the topic name.
        if (!string.IsNullOrEmpty(request.SubscriptionName))
            request = request with { EntityName = request.SubscriptionName };

        var nsResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<IReadOnlyList<CoreMessage>>(nsResult.Error);

        var ns = nsResult.Value;
        var sqs = _clientFactory.GetSqsClient(ns);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var mapped = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var queueUrl = await ResolveQueueUrlAsync(sqs, request.EntityName, ct).ConfigureAwait(false);
                var messages = await PeekFromUrlAsync(sqs, queueUrl, request.MaxMessages, ct).ConfigureAwait(false);
                return MapToMessages(messages, request.NamespaceId, request.EntityName, fromDlq: false);
            }, linkedCts.Token).ConfigureAwait(false);

            _logger.LogDebug("Peeked {Count} messages from SQS queue {QueueName} (namespace {NamespaceId})",
                mapped.Count, SanitizeForLog(request.EntityName), request.NamespaceId);
            return Result.Success<IReadOnlyList<CoreMessage>>(mapped);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("SQS peek timed out after {Seconds}s for queue {QueueName}", OperationTimeoutSeconds, SanitizeForLog(request.EntityName));
            return Result.Failure<IReadOnlyList<CoreMessage>>(Error.ExternalService(
                "AWS.SQS.Timeout", $"SQS operation timed out after {OperationTimeoutSeconds}s."));
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error peeking messages from {QueueName}", SanitizeForLog(request.EntityName));
            return Result.Failure<IReadOnlyList<CoreMessage>>(Error.ExternalService(
                "AWS.SQS.PeekFailed", $"SQS error: {ex.Message}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error peeking SQS messages from {QueueName}", SanitizeForLog(request.EntityName));
            return Result.Failure<IReadOnlyList<CoreMessage>>(Error.Internal("AWS.SQS.UnexpectedError", ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<CoreMessage>>> PeekDeadLetterMessagesAsync(
        GetMessagesRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!string.IsNullOrEmpty(request.SubscriptionName))
            request = request with { EntityName = request.SubscriptionName };

        var nsResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<IReadOnlyList<CoreMessage>>(nsResult.Error);

        var ns = nsResult.Value;
        var sqs = _clientFactory.GetSqsClient(ns);

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var mapped = await _resiliencePipeline.ExecuteAsync(async ct =>
            {
                var sourceUrl = await ResolveQueueUrlAsync(sqs, request.EntityName, ct).ConfigureAwait(false);
                var dlqUrl = await ResolveDlqUrlAsync(sqs, sourceUrl, ct).ConfigureAwait(false);

                if (dlqUrl is null)
                    return (IReadOnlyList<CoreMessage>?)null;

                var messages = await PeekFromUrlAsync(sqs, dlqUrl, request.MaxMessages, ct).ConfigureAwait(false);
                return MapToMessages(messages, request.NamespaceId, request.EntityName, fromDlq: true);
            }, linkedCts.Token).ConfigureAwait(false);

            if (mapped is null)
            {
                _logger.LogWarning("Queue {QueueName} has no DLQ configured (no RedrivePolicy)", SanitizeForLog(request.EntityName));
                return Result.Success<IReadOnlyList<CoreMessage>>(Array.Empty<CoreMessage>());
            }

            _logger.LogDebug("Peeked {Count} DLQ messages from {QueueName} (namespace {NamespaceId})",
                mapped.Count, SanitizeForLog(request.EntityName), request.NamespaceId);
            return Result.Success<IReadOnlyList<CoreMessage>>(mapped);
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("SQS DLQ peek timed out after {Seconds}s for queue {QueueName}", OperationTimeoutSeconds, SanitizeForLog(request.EntityName));
            return Result.Failure<IReadOnlyList<CoreMessage>>(Error.ExternalService(
                "AWS.SQS.Timeout", $"SQS DLQ operation timed out after {OperationTimeoutSeconds}s."));
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error peeking DLQ messages from {QueueName}", SanitizeForLog(request.EntityName));
            return Result.Failure<IReadOnlyList<CoreMessage>>(Error.ExternalService(
                "AWS.SQS.DlqPeekFailed", $"SQS error: {ex.Message}"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error peeking SQS DLQ messages from {QueueName}", SanitizeForLog(request.EntityName));
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

        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(OperationTimeoutSeconds));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        try
        {
            var queueUrl = await ResolveQueueUrlAsync(sqs, entityName, linkedCts.Token).ConfigureAwait(false);
            var attrs = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
            {
                QueueUrl = queueUrl,
                AttributeNames = new List<string> { "ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible" }
            }, linkedCts.Token).ConfigureAwait(false);

            var visible = (long)attrs.ApproximateNumberOfMessages;
            var inFlight = (long)attrs.ApproximateNumberOfMessagesNotVisible;
            return Result.Success(visible + inFlight);
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error getting message count for {QueueName}", SanitizeForLog(entityName));
            return Result.Failure<long>(Error.ExternalService("AWS.SQS.CountFailed", ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<int>> DeadLetterMessagesAsync(
        DeadLetterRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(request.SubscriptionName))
            request = request with { EntityName = request.SubscriptionName };

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

            // Manual dead-lettering: receive from the source, copy to the DLQ, delete
            // from the source. Uses the same long-poll scan discipline as peek — a
            // single short-poll receive samples only a subset of SQS hosts and often
            // returns empty even when the queue visibly holds messages, which the UI
            // surfaced as "no active messages to dead-letter".
            var count = Math.Min(request.ValidatedMessageCount, SqsMaxBatchSize);
            var deadLettered = 0;
            var receivedById = new Dictionary<string, SqsMessage>(StringComparer.Ordinal);
            var movedIds = new HashSet<string>(StringComparer.Ordinal);
            var quietRounds = 0;
            var gate = GetScanGate(sourceUrl);
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                for (var i = 0; i < MaxScanBatches && receivedById.Count < count; i++)
                {
                    var received = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                    {
                        QueueUrl = sourceUrl,
                        MaxNumberOfMessages = SqsMaxBatchSize,
                        VisibilityTimeout = ScanLockSeconds,
                        WaitTimeSeconds = 1,    // long poll: samples all servers, not a subset
                        MessageSystemAttributeNames = new List<string> { "All" },
                        MessageAttributeNames = new List<string> { "All" }
                    }, cancellationToken).ConfigureAwait(false);

                    var added = 0;
                    foreach (var msg in received.Messages)
                    {
                        var isNew = !receivedById.ContainsKey(msg.MessageId);
                        receivedById[msg.MessageId] = msg;
                        if (isNew)
                            added++;
                    }

                    if (added == 0)
                    {
                        if (++quietRounds >= 5)
                            break;
                    }
                    else
                    {
                        quietRounds = 0;
                    }
                }

                // Send each to DLQ with reason attribute, then delete from source
                foreach (var msg in receivedById.Values.Take(count))
                {
                    await sqs.SendMessageAsync(new SqsSend
                    {
                        QueueUrl = dlqUrl,
                        MessageBody = msg.Body,
                        MessageAttributes = BuildDeadLetterAttributes(msg, request.Reason, request.ErrorDescription)
                    }, cancellationToken).ConfigureAwait(false);

                    await sqs.DeleteMessageAsync(new DeleteMessageRequest
                    {
                        QueueUrl = sourceUrl,
                        ReceiptHandle = msg.ReceiptHandle
                    }, cancellationToken).ConfigureAwait(false);

                    movedIds.Add(msg.MessageId);
                    deadLettered++;
                }
            }
            finally
            {
                try
                {
                    // Release messages that were received but not moved; if release
                    // fails they reappear on their own once the scan lock expires.
                    var leftovers = receivedById.Values
                        .Where(m => !movedIds.Contains(m.MessageId))
                        .ToList();
                    foreach (var chunk in leftovers.Chunk(SqsMaxBatchSize))
                    {
                        try
                        {
                            await sqs.ChangeMessageVisibilityBatchAsync(new ChangeMessageVisibilityBatchRequest
                            {
                                QueueUrl = sourceUrl,
                                Entries = chunk.Select((m, idx) => new ChangeMessageVisibilityBatchRequestEntry
                                {
                                    Id = idx.ToString(),
                                    ReceiptHandle = m.ReceiptHandle,
                                    VisibilityTimeout = 0
                                }).ToList()
                            }, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (AmazonSQSException ex)
                        {
                            _logger.LogWarning(ex, "Failed to release visibility for scanned messages; they will reappear after {Seconds}s", ScanLockSeconds);
                        }
                    }
                }
                finally
                {
                    gate.Release();
                }
            }

            _logger.LogInformation("Dead-lettered {Count} messages from {QueueName}", deadLettered, SanitizeForLog(request.EntityName));
            return Result.Success(deadLettered);
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error dead-lettering messages from {QueueName}", SanitizeForLog(request.EntityName));
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
        if (!string.IsNullOrEmpty(subscriptionName))
            entityName = subscriptionName;

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

            // Sequence numbers are derived from the stable SQS MessageId, so the target
            // can be located by scanning the DLQ — no prior peek or cache required.
            var target = await FindAndLockMessageAsync(sqs, dlqUrl, sequenceNumber, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                _logger.LogWarning("Message with sequence {Seq} not found in DLQ for {QueueName}", sequenceNumber, SanitizeForLog(entityName));
                return Result.Failure(Error.NotFound("AWS.SQS.MessageNotFound",
                    $"Message {sequenceNumber} was not found in the DLQ — it may have been consumed, replayed, or expired."));
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

            _logger.LogInformation("Replayed message {Seq} from DLQ to {QueueName}", sequenceNumber, SanitizeForLog(entityName));
            return Result.Success();
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error replaying message {Seq} for {QueueName}", sequenceNumber, SanitizeForLog(entityName));
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
        if (!string.IsNullOrEmpty(subscriptionName))
            entityName = subscriptionName;

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

            var target = await FindAndLockMessageAsync(sqs, targetUrl, sequenceNumber, cancellationToken).ConfigureAwait(false);
            if (target is null)
            {
                return Result.Failure(Error.NotFound("AWS.SQS.MessageNotFound",
                    $"Message {sequenceNumber} was not found in the queue — it may have been consumed, replayed, or expired."));
            }

            await sqs.DeleteMessageAsync(new DeleteMessageRequest
            {
                QueueUrl = targetUrl,
                ReceiptHandle = target.ReceiptHandle
            }, cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Purged message {Seq} from {Queue}", sequenceNumber, SanitizeForLog(entityName));
            return Result.Success();
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error purging message {Seq} for {QueueName}", sequenceNumber, SanitizeForLog(entityName));
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
            SanitizeForLog(entityName));

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
            _logger.LogError(ex, "SQS error getting visibility status for {QueueName}", SanitizeForLog(queueName));
            return Result.Failure<SqsVisibilityInfo>(Error.ExternalService("AWS.SQS.VisibilityFailed", ex.Message));
        }
    }

    // ── Private helpers ───────────────────────────────────────────────────────
    private static string SanitizeForLog(string? value)
        => (value ?? string.Empty)
            .Replace("\r", string.Empty, StringComparison.Ordinal)
            .Replace("\n", string.Empty, StringComparison.Ordinal);
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
        if (_dlqUrlCache.Count >= DlqUrlCacheMaxSize)
        {
            // Evict an arbitrary entry when the URL cache is at capacity
            var firstKey = _dlqUrlCache.Keys.FirstOrDefault();
            if (firstKey is not null)
                _dlqUrlCache.TryRemove(firstKey, out _);
        }
        _dlqUrlCache.TryAdd(sourceQueueUrl, dlqUrl);
        return dlqUrl;
    }

    private async Task<List<SqsMessage>> PeekFromUrlAsync(
        IAmazonSQS sqs, string queueUrl, int maxMessages, CancellationToken ct)
    {
        var allMessages = new List<SqsMessage>();
        var receivedByMessageId = new Dictionary<string, SqsMessage>(StringComparer.Ordinal);
        var quietRounds = 0;

        var gate = GetScanGate(queueUrl);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Each SQS receive samples a subset of the queue's distributed hosts, so
            // enumerating even a small queue takes several rounds. Hold the scan lock
            // while iterating so each message is received exactly once per peek —
            // every receive bumps ApproximateReceiveCount, and re-receiving the same
            // messages round after round trips the queue's redrive policy, silently
            // dead-lettering healthy messages. Locks are released in the finally.
            for (var i = 0; i < MaxScanBatches && allMessages.Count < maxMessages; i++)
            {
                var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = SqsMaxBatchSize,
                    VisibilityTimeout = ScanLockSeconds,
                    WaitTimeSeconds = 1,    // long poll: samples all servers, not a subset
                    MessageSystemAttributeNames = new List<string> { "All" },
                    MessageAttributeNames = new List<string> { "All" }
                }, ct).ConfigureAwait(false);

                var added = 0;
                foreach (var message in response.Messages)
                {
                    // At-least-once delivery can still duplicate; keep the latest
                    // receipt handle, which is the only one valid for release.
                    var isNew = !receivedByMessageId.ContainsKey(message.MessageId);
                    receivedByMessageId[message.MessageId] = message;

                    if (isNew && allMessages.Count < maxMessages)
                    {
                        allMessages.Add(message);
                        added++;
                    }
                }

                if (added == 0)
                {
                    if (++quietRounds >= 5)
                        break;
                }
                else
                {
                    quietRounds = 0;
                }
            }
        }
        finally
        {
            try
            {
                // Peek must not consume: make everything we received visible again; if
                // release fails they reappear on their own once the scan lock expires.
                // Runs even when the caller cancelled, so locks are not left behind.
                foreach (var chunk in receivedByMessageId.Values.Chunk(SqsMaxBatchSize))
                {
                    try
                    {
                        await sqs.ChangeMessageVisibilityBatchAsync(new ChangeMessageVisibilityBatchRequest
                        {
                            QueueUrl = queueUrl,
                            Entries = chunk.Select((m, idx) => new ChangeMessageVisibilityBatchRequestEntry
                            {
                                Id = idx.ToString(),
                                ReceiptHandle = m.ReceiptHandle,
                                VisibilityTimeout = 0
                            }).ToList()
                        }, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (AmazonSQSException ex)
                    {
                        _logger.LogWarning(ex, "Failed to release visibility for peeked messages; they will reappear after {Seconds}s", ScanLockSeconds);
                    }
                }
            }
            finally
            {
                gate.Release();
            }
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
            // Derive a stable long ID from the SQS MessageId, so a message keeps the same
            // sequence number across peeks/restarts and replay/purge can find it by scanning.
            var seqNum = ComputeSequenceNumber(msg.MessageId);

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

            // The dead-letter markers stamped by manual dead-lettering are ServiceHub
            // metadata, not user properties — promote them to the dedicated fields.
            msg.MessageAttributes.TryGetValue(DeadLetterReasonAttribute, out var dlReason);
            msg.MessageAttributes.TryGetValue(DeadLetterDescriptionAttribute, out var dlDescription);

            // Map MessageAttributes → ApplicationProperties
            var appProps = msg.MessageAttributes
                .Where(kvp => kvp.Key is not (DeadLetterReasonAttribute or DeadLetterDescriptionAttribute))
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => (object)kvp.Value.StringValue);

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
                DeadLetterReason = dlReason?.StringValue,
                DeadLetterErrorDescription = dlDescription?.StringValue,
                State = ServiceHub.Core.Enums.MessageState.Active
            });
        }

        return mapped;
    }

    /// <summary>
    /// Copies the message's original attributes and stamps the dead-letter reason
    /// (and optional description). SQS caps messages at 10 attributes, so excess
    /// originals are spilled into the same overflow JSON property the send path
    /// uses, merging with an existing overflow attribute if one is present.
    /// </summary>
    private static Dictionary<string, MessageAttributeValue> BuildDeadLetterAttributes(
        SqsMessage msg, string reason, string? errorDescription)
    {
        var attrs = new Dictionary<string, MessageAttributeValue>(msg.MessageAttributes)
        {
            [DeadLetterReasonAttribute] = new MessageAttributeValue { DataType = "String", StringValue = reason }
        };
        if (!string.IsNullOrEmpty(errorDescription))
            attrs[DeadLetterDescriptionAttribute] = new MessageAttributeValue { DataType = "String", StringValue = errorDescription };

        if (attrs.Count <= MaxMessageAttributes)
            return attrs;

        var overflow = attrs.TryGetValue(OverflowAttributeName, out var existing) && existing.StringValue is { Length: > 0 }
            ? System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(existing.StringValue) ?? new Dictionary<string, string>()
            : new Dictionary<string, string>();

        var spillKeys = attrs.Keys
            .Where(k => k is not (DeadLetterReasonAttribute or DeadLetterDescriptionAttribute or OverflowAttributeName))
            .OrderBy(k => k, StringComparer.Ordinal)
            .Take(attrs.Count - MaxMessageAttributes + (attrs.ContainsKey(OverflowAttributeName) ? 0 : 1))
            .ToList();

        foreach (var key in spillKeys)
        {
            overflow[key] = attrs[key].StringValue ?? string.Empty;
            attrs.Remove(key);
        }

        attrs[OverflowAttributeName] = new MessageAttributeValue
        {
            DataType = "String",
            StringValue = System.Text.Json.JsonSerializer.Serialize(overflow)
        };
        return attrs;
    }

    private static long ComputeSequenceNumber(string messageId)
    {
        // Use a stable 64-bit hash derived from SHA-256 so sequence numbers:
        //  1. Are consistent across process restarts (unlike GetHashCode which is randomized)
        //  2. Have negligible collision probability (2^63 space vs 2^31 for GetHashCode)
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(messageId));
        return BitConverter.ToInt64(hash, 0) & long.MaxValue; // keep positive
    }

    /// <summary>
    /// Scans a queue for the message whose MessageId hashes to <paramref name="sequenceNumber"/>,
    /// locking received messages behind a visibility window during the scan. The target (if found)
    /// stays locked and is returned with a fresh receipt handle; all other messages are released.
    /// </summary>
    private async Task<SqsMessage?> FindAndLockMessageAsync(
        IAmazonSQS sqs, string queueUrl, long sequenceNumber, CancellationToken ct)
    {
        SqsMessage? target = null;
        var nonTargets = new List<SqsMessage>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        var gate = GetScanGate(queueUrl);
        await gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            for (var i = 0; i < MaxScanBatches && target is null; i++)
            {
                var response = await sqs.ReceiveMessageAsync(new ReceiveMessageRequest
                {
                    QueueUrl = queueUrl,
                    MaxNumberOfMessages = SqsMaxBatchSize,
                    VisibilityTimeout = ScanLockSeconds,
                    WaitTimeSeconds = 1,    // long poll: samples all servers, not a subset
                    MessageSystemAttributeNames = new List<string> { "All" },
                    MessageAttributeNames = new List<string> { "All" }
                }, ct).ConfigureAwait(false);

                if (response.Messages.Count == 0)
                    break;

                var progressed = false;
                foreach (var message in response.Messages)
                {
                    if (!seenIds.Add(message.MessageId))
                        continue;

                    progressed = true;
                    if (ComputeSequenceNumber(message.MessageId) == sequenceNumber)
                        target = message;
                    else
                        nonTargets.Add(message);
                }

                if (!progressed)
                    break;
            }
        }
        finally
        {
            try
            {
                // Release the messages we merely inspected; if this fails they become
                // visible again on their own once the scan lock expires.
                foreach (var chunk in nonTargets.Chunk(SqsMaxBatchSize))
                {
                    try
                    {
                        await sqs.ChangeMessageVisibilityBatchAsync(new ChangeMessageVisibilityBatchRequest
                        {
                            QueueUrl = queueUrl,
                            Entries = chunk.Select((m, idx) => new ChangeMessageVisibilityBatchRequestEntry
                            {
                                Id = idx.ToString(),
                                ReceiptHandle = m.ReceiptHandle,
                                VisibilityTimeout = 0
                            }).ToList()
                        }, CancellationToken.None).ConfigureAwait(false);
                    }
                    catch (AmazonSQSException ex)
                    {
                        _logger.LogWarning(ex, "Failed to release visibility for scanned messages; they will reappear after {Seconds}s", ScanLockSeconds);
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
}
