using System.Text.Json;
using Amazon.SQS;
using Amazon.SQS.Model;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.Aws;

/// <summary>
/// Scans SQS queues to identify those with a dead-letter queue configuration.
/// Equivalent to the Azure DLQ monitor worker for AWS namespaces.
/// </summary>
public sealed class AwsDlqDetector
{
    private readonly IAwsClientFactory _clientFactory;
    private readonly INamespaceRepository _namespaceRepository;
    private readonly ILogger<AwsDlqDetector> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="AwsDlqDetector"/>.
    /// </summary>
    /// <param name="clientFactory">Factory for creating SQS clients per namespace.</param>
    /// <param name="namespaceRepository">Repository for namespace lookups.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public AwsDlqDetector(
        IAwsClientFactory clientFactory,
        INamespaceRepository namespaceRepository,
        ILogger<AwsDlqDetector> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _namespaceRepository = namespaceRepository ?? throw new ArgumentNullException(nameof(namespaceRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Lists all queues in the namespace that have a <c>RedrivePolicy</c> configured.
    /// Returns a dictionary keyed by queue name, with the DLQ ARN as the value.
    /// </summary>
    /// <param name="namespaceId">The namespace identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A result containing a read-only dictionary of queue name → DLQ ARN pairs.</returns>
    public async Task<Result<IReadOnlyDictionary<string, string>>> ListDeadLetterQueuesAsync(
        Guid namespaceId,
        CancellationToken cancellationToken = default)
    {
        var nsResult = await _namespaceRepository.GetByIdAsync(namespaceId, cancellationToken).ConfigureAwait(false);
        if (nsResult.IsFailure)
            return Result.Failure<IReadOnlyDictionary<string, string>>(nsResult.Error);

        var sqs = _clientFactory.GetSqsClient(nsResult.Value);
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var listResponse = await sqs.ListQueuesAsync(new ListQueuesRequest(), cancellationToken).ConfigureAwait(false);

            foreach (var queueUrl in listResponse.QueueUrls)
            {
                try
                {
                    var attrs = await sqs.GetQueueAttributesAsync(new GetQueueAttributesRequest
                    {
                        QueueUrl = queueUrl,
                        AttributeNames = ["RedrivePolicy"]
                    }, cancellationToken).ConfigureAwait(false);

                    if (!attrs.Attributes.TryGetValue("RedrivePolicy", out var redriveJson)
                        || string.IsNullOrEmpty(redriveJson))
                        continue;

                    using var doc = JsonDocument.Parse(redriveJson);
                    if (!doc.RootElement.TryGetProperty("deadLetterTargetArn", out var arnElem))
                        continue;

                    var dlqArn = arnElem.GetString();
                    if (string.IsNullOrEmpty(dlqArn))
                        continue;

                    var queueName = queueUrl.Split('/').LastOrDefault() ?? queueUrl;
                    result[queueName] = dlqArn;
                }
                catch (AmazonSQSException ex)
                {
                    _logger.LogWarning(ex, "Could not read RedrivePolicy for queue {QueueUrl}", queueUrl);
                }
            }

            _logger.LogDebug("Found {Count} DLQ-configured queues in namespace {NamespaceId}",
                result.Count, namespaceId);
            return Result.Success<IReadOnlyDictionary<string, string>>(result);
        }
        catch (AmazonSQSException ex)
        {
            _logger.LogError(ex, "SQS error listing DLQ queues for namespace {NamespaceId}", namespaceId);
            return Result.Failure<IReadOnlyDictionary<string, string>>(
                Error.ExternalService("AWS.SQS.DlqListFailed", ex.Message));
        }
    }
}
