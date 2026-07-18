using Amazon.SQS;
using Amazon.SimpleNotificationService;
using Microsoft.Extensions.Logging;
using Polly;
using ServiceHub.Infrastructure.Aws.Resilience;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;
using SqsSendRequest = Amazon.SQS.Model.SendMessageRequest;
using SqsBatchEntry = Amazon.SQS.Model.SendMessageBatchRequestEntry;
using SqsBatchRequest = Amazon.SQS.Model.SendMessageBatchRequest;
using SqsMessageAttr = Amazon.SQS.Model.MessageAttributeValue;
using SnsMessageAttr = Amazon.SimpleNotificationService.Model.MessageAttributeValue;
using SnsPublishRequest = Amazon.SimpleNotificationService.Model.PublishRequest;
using SqsGetQueueUrlRequest = Amazon.SQS.Model.GetQueueUrlRequest;

namespace ServiceHub.Infrastructure.Aws;

/// <summary>
/// Implements <see cref="IMessageSender"/> for Amazon SQS queues and SNS topics.
/// <para>
/// FIFO queue support: when <see cref="SendMessageRequest.EntityName"/> ends with <c>.fifo</c>,
/// the sender adds the required <c>MessageGroupId</c> and <c>MessageDeduplicationId</c> attributes.
/// </para>
/// <para>
/// SNS publish: when <see cref="SendMessageRequest.EntityName"/> begins with <c>arn:aws:sns</c>,
/// the sender uses <see cref="IAmazonSimpleNotificationService"/> instead of SQS.
/// </para>
/// </summary>
public sealed class AwsMessageSender : IMessageSender
{
    private readonly IAwsClientFactory _clientFactory;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<AwsMessageSender> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    private const int SqsMaxBatchSize = 10;

    /// <summary>
    /// Initialises a new instance of <see cref="AwsMessageSender"/>.
    /// </summary>
    /// <param name="clientFactory">Factory that creates IAmazonSQS/IAmazonSNS clients per namespace.</param>
    /// <param name="namespaceRepository">Repository for resolving namespace credentials by ID.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AwsMessageSender(
        IAwsClientFactory clientFactory,
        INamespaceRepository namespaceRepository,
        ILogger<AwsMessageSender> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = AwsResiliencePipeline.Create(_logger);
    }

    /// <inheritdoc/>
    public async Task<Result> SendAsync(
        SendMessageRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.NamespaceId is null || request.EntityName is null)
            return Result.Failure(Error.Validation("AWS.SQS.InvalidRequest",
                "NamespaceId and EntityName are required."));

        var nsResult = await _namespaceRepository.GetByIdAsync(request.NamespaceId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure(nsResult.Error);

        try
        {
            // SNS publish if the entity is a topic (flagged by the topics endpoint,
            // or given directly as a topic ARN)
            if (request.IsTopic ||
                request.EntityName.StartsWith("arn:aws:sns", StringComparison.OrdinalIgnoreCase))
            {
                var sns = _clientFactory.GetSnsClient(nsResult.Value);

                var topicArn = request.EntityName;
                if (!topicArn.StartsWith("arn:aws:sns", StringComparison.OrdinalIgnoreCase))
                {
                    var topicsResponse = await _resiliencePipeline.ExecuteAsync(async ct =>
                        await sns.ListTopicsAsync(ct).ConfigureAwait(false),
                        cancellationToken).ConfigureAwait(false);

                    var match = topicsResponse.Topics.FirstOrDefault(t =>
                        t.TopicArn.EndsWith(":" + request.EntityName, StringComparison.OrdinalIgnoreCase));
                    if (match is null)
                        return Result.Failure(Error.NotFound("AWS.SNS.TopicNotFound",
                            $"SNS topic '{request.EntityName}' was not found."));

                    topicArn = match.TopicArn;
                }

                var snsRequest = new SnsPublishRequest
                {
                    TopicArn = topicArn,
                    Message = request.Body,
                    MessageAttributes = BuildSnsMessageAttributes(request)
                };
                await _resiliencePipeline.ExecuteAsync(async ct =>
                    await sns.PublishAsync(snsRequest, ct).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);

                _logger.LogInformation("Published message to SNS topic {TopicArn}", LogRedactor.SanitiseForLog(request.EntityName));
                return Result.Success();
            }

            // SQS send
            var sqs = _clientFactory.GetSqsClient(nsResult.Value);
            var queueUrl = await ResolveQueueUrlAsync(sqs, request.EntityName, cancellationToken).ConfigureAwait(false);
            var sqsRequest = BuildSqsRequest(queueUrl, request);
            await _resiliencePipeline.ExecuteAsync(async ct =>
                await sqs.SendMessageAsync(sqsRequest, ct).ConfigureAwait(false),
                cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Sent message to SQS queue {QueueName}", LogRedactor.SanitiseForLog(request.EntityName));
            return Result.Success();
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error sending message to {QueueName}", LogRedactor.SanitiseForLog(request.EntityName));
            return Result.Failure(Error.ExternalService("AWS.SQS.SendFailed", ex.Message));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error sending message to {QueueName}", LogRedactor.SanitiseForLog(request.EntityName));
            return Result.Failure(Error.Internal("AWS.SQS.UnexpectedError", ex.Message));
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

        // All requests in a batch must share the same namespace and entity
        var first = requestList[0];
        if (first.NamespaceId is null || first.EntityName is null)
            return Result.Failure(Error.Validation("AWS.SQS.InvalidRequest",
                "NamespaceId and EntityName are required for batch send."));

        var nsResult = await _namespaceRepository.GetByIdAsync(first.NamespaceId.Value, cancellationToken)
            .ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure(nsResult.Error);

        var sqs = _clientFactory.GetSqsClient(nsResult.Value);
        var queueUrl = await ResolveQueueUrlAsync(sqs, first.EntityName, cancellationToken).ConfigureAwait(false);

        // Split into chunks of 10 (SQS hard limit per batch call)
        var chunks = requestList.Chunk(SqsMaxBatchSize);
        var failed = new List<string>();

        foreach (var chunk in chunks)
        {
            var entries = chunk.Select((req, idx) => new SqsBatchEntry
            {
                Id = idx.ToString(),
                MessageBody = req.Body,
                MessageAttributes = BuildSqsMessageAttributes(req),
                MessageGroupId = IsFifo(first.EntityName) ? (req.SessionId ?? "default") : null,
                MessageDeduplicationId = IsFifo(first.EntityName) ? Guid.NewGuid().ToString() : null
            }).ToList();

            try
            {
                var batchResponse = await _resiliencePipeline.ExecuteAsync(async ct =>
                    await sqs.SendMessageBatchAsync(new SqsBatchRequest
                    {
                        QueueUrl = queueUrl,
                        Entries = entries
                    }, ct).ConfigureAwait(false),
                    cancellationToken).ConfigureAwait(false);

                failed.AddRange(batchResponse.Failed.Select(f => $"[{f.Id}] {f.Message}"));
            }
            catch (AmazonSQSException ex)
            {
                _logger.LogError(ex, "SQS error in batch send chunk for {QueueName}", LogRedactor.SanitiseForLog(first.EntityName));
                return Result.Failure(Error.ExternalService("AWS.SQS.BatchSendFailed", ex.Message));
            }
        }

        if (failed.Count > 0)
        {
            _logger.LogWarning("Batch send partial failure for {QueueName}: {Errors}",
                LogRedactor.SanitiseForLog(first.EntityName), string.Join("; ", failed));
            return Result.Failure(Error.ExternalService("AWS.SQS.BatchPartialFailure",
                $"Batch send had {failed.Count} failures: {string.Join("; ", failed)}"));
        }

        _logger.LogInformation("Batch sent {Count} messages to {QueueName}", requestList.Count, LogRedactor.SanitiseForLog(first.EntityName));
        return Result.Success();
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static async Task<string> ResolveQueueUrlAsync(
        IAmazonSQS sqs, string queueName, CancellationToken ct)
    {
        if (queueName.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return queueName;

        var response = await sqs.GetQueueUrlAsync(new SqsGetQueueUrlRequest { QueueName = queueName }, ct)
            .ConfigureAwait(false);
        return response.QueueUrl;
    }

    private static SqsSendRequest BuildSqsRequest(
        string queueUrl, SendMessageRequest request)
    {
        var sqsRequest = new SqsSendRequest
        {
            QueueUrl = queueUrl,
            MessageBody = request.Body,
            MessageAttributes = BuildSqsMessageAttributes(request)
        };

        if (IsFifo(request.EntityName!))
        {
            sqsRequest.MessageGroupId = request.SessionId ?? "default";
            sqsRequest.MessageDeduplicationId = Guid.NewGuid().ToString();
        }

        return sqsRequest;
    }

    // SQS and SNS both reject messages with more than 10 message attributes.
    private const int MaxMessageAttributes = 10;
    private const string OverflowAttributeName = "shs-overflow-properties";

    /// <summary>
    /// Flattens application properties to string pairs within the provider's
    /// 10-attribute limit. When there are more, the first nine are kept as-is and
    /// the remainder are folded into a single JSON attribute so no data is lost.
    /// </summary>
    private static List<KeyValuePair<string, string>> FlattenProperties(SendMessageRequest request)
    {
        if (request.ApplicationProperties is null || request.ApplicationProperties.Count == 0)
            return [];

        var pairs = request.ApplicationProperties
            .Select(kvp => new KeyValuePair<string, string?>(kvp.Key, kvp.Value?.ToString()))
            .Where(kvp => kvp.Value is not null)
            .Select(kvp => new KeyValuePair<string, string>(kvp.Key, kvp.Value!))
            .ToList();

        if (pairs.Count <= MaxMessageAttributes)
            return pairs;

        var kept = pairs.Take(MaxMessageAttributes - 1).ToList();
        var overflow = pairs.Skip(MaxMessageAttributes - 1)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        kept.Add(new KeyValuePair<string, string>(
            OverflowAttributeName,
            System.Text.Json.JsonSerializer.Serialize(overflow)));
        return kept;
    }

    private static Dictionary<string, SqsMessageAttr> BuildSqsMessageAttributes(
        SendMessageRequest request)
        => FlattenProperties(request).ToDictionary(
            kvp => kvp.Key,
            kvp => new SqsMessageAttr { DataType = "String", StringValue = kvp.Value });

    private static Dictionary<string, SnsMessageAttr> BuildSnsMessageAttributes(
        SendMessageRequest request)
        => FlattenProperties(request).ToDictionary(
            kvp => kvp.Key,
            kvp => new SnsMessageAttr { DataType = "String", StringValue = kvp.Value });

    private static bool IsFifo(string entityName) =>
        entityName.EndsWith(".fifo", StringComparison.OrdinalIgnoreCase);
}
