using Amazon.SQS;
using Amazon.SQS.Model;
using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Microsoft.Extensions.Logging;
using Polly;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Aws.Models;
using ServiceHub.Infrastructure.Aws.Resilience;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Aws;

/// <summary>
/// Implements <see cref="ICloudMessagingProvider"/> for Amazon SQS and SNS.
/// </summary>
public sealed class AwsMessagingProvider : ICloudMessagingProvider
{
    private readonly IAwsClientFactory _clientFactory;
    private readonly AwsMessageReceiver _receiver;
    private readonly AwsMessageSender _sender;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<AwsMessagingProvider> _logger;
    private readonly ResiliencePipeline _resiliencePipeline;

    /// <summary>
    /// Initialises a new instance of <see cref="AwsMessagingProvider"/>.
    /// </summary>
    /// <param name="clientFactory">Factory for creating SQS/SNS clients.</param>
    /// <param name="receiver">The AWS message receiver.</param>
    /// <param name="sender">The AWS message sender.</param>
    /// <param name="namespaceRepository">Repository for namespace lookups.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AwsMessagingProvider(
        IAwsClientFactory clientFactory,
        AwsMessageReceiver receiver,
        AwsMessageSender sender,
        INamespaceRepository namespaceRepository,
        ILogger<AwsMessagingProvider> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _resiliencePipeline = AwsResiliencePipeline.Create(_logger);
    }

    /// <inheritdoc/>
    public CloudProviderType ProviderType => CloudProviderType.Aws;

    /// <inheritdoc/>
    public ProviderCapabilities Capabilities => ProviderCapabilities.Aws;

    /// <inheritdoc/>
    public IMessageReceiver GetMessageReceiver() => _receiver;

    /// <inheritdoc/>
    public IMessageSender GetMessageSender() => _sender;

    /// <inheritdoc/>
    public async Task<Result> ValidateConnectionAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        _logger.LogDebug("Validating AWS SQS connection for namespace {NamespaceId}", ns.Id);

        try
        {
            var sqs = _clientFactory.GetSqsClient(ns);
            // List queues with max 1 result — validates credentials without requiring a specific queue.
            await _resiliencePipeline.ExecuteAsync(
                async token => await sqs.ListQueuesAsync(new ListQueuesRequest { MaxResults = 1 }, token).ConfigureAwait(false),
                ct).ConfigureAwait(false);
            _logger.LogInformation("AWS SQS connection validated for namespace {NamespaceId}", ns.Id);
            return Result.Success();
        }
        catch (AmazonSQSException ex) when (
            ex.ErrorCode == "InvalidClientTokenId" ||
            ex.ErrorCode == "AccessDenied" ||
            ex.ErrorCode == "AuthFailure")
        {
            _logger.LogWarning("AWS credential validation failed for namespace {NamespaceId}: {ErrorCode}", ns.Id, ex.ErrorCode);
            return Result.Failure(Error.Validation("AWS.SQS.AuthFailed",
                $"Invalid AWS credentials: {ex.Message}"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating AWS connection for namespace {NamespaceId}", ns.Id);
            return Result.Failure(Error.ExternalService("AWS.SQS.ValidationFailed", ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<IReadOnlyList<CloudEntity>>> ListEntitiesAsync(
        Guid namespaceId,
        CancellationToken ct)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, ct).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<IReadOnlyList<CloudEntity>>(nsResult.Error);

        var ns = nsResult.Value;
        var entities = new List<CloudEntity>();

        try
        {
            var sqs = _clientFactory.GetSqsClient(ns);

            // List all SQS queues
            var listResponse = await _resiliencePipeline.ExecuteAsync(
                async token => await sqs.ListQueuesAsync(new ListQueuesRequest(), token).ConfigureAwait(false),
                ct).ConfigureAwait(false);

            var queueSnapshots = new List<(string Name, long ActiveCount, string? RedriveTargetName)>();

            foreach (var queueUrl in listResponse.QueueUrls)
            {
                try
                {
                    var attrs = await _resiliencePipeline.ExecuteAsync(
                        async token => await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
                        {
                            QueueUrl = queueUrl,
                            AttributeNames = new List<string>
                            {
                                "ApproximateNumberOfMessages",
                                "ApproximateNumberOfMessagesNotVisible",
                                "RedrivePolicy"
                            }
                        }, token).ConfigureAwait(false),
                        ct).ConfigureAwait(false);

                    var visible = (long)attrs.ApproximateNumberOfMessages;
                    var inFlight = (long)attrs.ApproximateNumberOfMessagesNotVisible;

                    // Extract queue name from URL
                    var queueName = queueUrl.Split('/').LastOrDefault() ?? queueUrl;

                    queueSnapshots.Add((queueName, visible + inFlight, ParseRedriveTargetName(attrs)));
                }
                catch (AmazonSQSException ex)
                {
                    _logger.LogWarning(ex, "Could not get attributes for queue {QueueUrl}", queueUrl);
                }
            }

            // SQS surfaces the DLQ as a separate queue; report its depth as the source
            // queue's dead-letter count so the UI's Azure-style DLQ tab shows real numbers.
            var countsByName = queueSnapshots.ToDictionary(q => q.Name, q => q.ActiveCount, StringComparer.OrdinalIgnoreCase);
            foreach (var (queueName, activeCount, redriveTargetName) in queueSnapshots)
            {
                entities.Add(new CloudEntity
                {
                    Name = queueName,
                    EntityType = "Queue",
                    ActiveMessageCount = activeCount,
                    DeadLetterCount = redriveTargetName is not null &&
                                      countsByName.TryGetValue(redriveTargetName, out var dlqCount)
                        ? dlqCount
                        : 0,
                    Provider = CloudProviderType.Aws,
                    DeadLetterTargetName = redriveTargetName
                });
            }

            // List SNS topics
            try
            {
                var sns = _clientFactory.GetSnsClient(ns);
                var topicsResponse = await _resiliencePipeline.ExecuteAsync(
                    async token => await sns.ListTopicsAsync(token).ConfigureAwait(false),
                    ct).ConfigureAwait(false);

                foreach (var topic in topicsResponse.Topics)
                {
                    // ARNs contain ':' which breaks URL routing downstream — expose the friendly name.
                    var topicName = topic.TopicArn[(topic.TopicArn.LastIndexOf(':') + 1)..];
                    entities.Add(new CloudEntity
                    {
                        Name = topicName,
                        EntityType = "SNS Topic",
                        Provider = CloudProviderType.Aws
                    });

                    try
                    {
                        var subsResponse = await _resiliencePipeline.ExecuteAsync(
                            async token => await sns.ListSubscriptionsByTopicAsync(topic.TopicArn, token).ConfigureAwait(false),
                            ct).ConfigureAwait(false);

                        foreach (var sub in subsResponse.Subscriptions)
                        {
                            var endpointName = string.IsNullOrEmpty(sub.Endpoint)
                                ? sub.Protocol
                                : sub.Endpoint[(sub.Endpoint.LastIndexOf(':') + 1)..];
                            entities.Add(new CloudEntity
                            {
                                Name = $"{topicName}/{endpointName}",
                                EntityType = "Subscription",
                                Provider = CloudProviderType.Aws
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not list SNS subscriptions for topic {TopicArn}", topic.TopicArn);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not list SNS topics for namespace {NamespaceId}", namespaceId);
            }

            return Result.Success<IReadOnlyList<CloudEntity>>(entities);
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error listing entities for namespace {NamespaceId}", namespaceId);
            return Result.Failure<IReadOnlyList<CloudEntity>>(Error.ExternalService("AWS.SQS.ListFailed", ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error listing entities for namespace {NamespaceId}", namespaceId);
            return Result.Failure<IReadOnlyList<CloudEntity>>(Error.ExternalService("AWS.SQS.ListFailed", ex.Message));
        }
    }

    // ── AWS-specific features ─────────────────────────────────────────────────

    /// <summary>
    /// Returns the SNS fanout map for a topic showing which subscriptions are confirmed.
    /// Useful for diagnosing missing message deliveries caused by unconfirmed subscriptions.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="topicArn">The full ARN of the SNS topic.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A <see cref="SnsFanoutMap"/> describing all subscriptions and their statuses.</returns>
    public async Task<Result<SnsFanoutMap>> GetSnsFanoutMapAsync(
        Guid namespaceId, string topicArn, CancellationToken ct)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, ct).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<SnsFanoutMap>(nsResult.Error);

        try
        {
            var sns = _clientFactory.GetSnsClient(nsResult.Value);
            var response = await _resiliencePipeline.ExecuteAsync(
                async token => await sns.ListSubscriptionsByTopicAsync(topicArn, token).ConfigureAwait(false),
                ct).ConfigureAwait(false);

            var subscriptions = response.Subscriptions
                .Select(sub => new SnsSubscriptionStatus(
                    SubscriptionArn: sub.SubscriptionArn,
                    Protocol: sub.Protocol,
                    Endpoint: sub.Endpoint,
                    Status: sub.SubscriptionArn == "PendingConfirmation" ? "PendingConfirmation" : "Confirmed"))
                .ToList();

            return Result.Success(new SnsFanoutMap(topicArn, subscriptions));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting SNS fanout map for topic {TopicArn}", topicArn);
            return Result.Failure<SnsFanoutMap>(Error.ExternalService("AWS.SNS.FanoutMapFailed", ex.Message));
        }
    }

    private static string? ParseRedriveTargetName(GetQueueAttributesResponse attrs)
    {
        if (attrs.Attributes is null ||
            !attrs.Attributes.TryGetValue("RedrivePolicy", out var redriveJson) ||
            string.IsNullOrWhiteSpace(redriveJson))
        {
            return null;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(redriveJson);
            if (doc.RootElement.TryGetProperty("deadLetterTargetArn", out var arnElement) &&
                arnElement.GetString() is { Length: > 0 } arn)
            {
                return arn[(arn.LastIndexOf(':') + 1)..];
            }
        }
        catch (System.Text.Json.JsonException)
        {
        }

        return null;
    }
}
