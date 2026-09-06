using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.Model;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.Aws.DlqObserver;

/// <summary>
/// Reads the AWS DLQ observer's DynamoDB log (`terraform/modules/aws/dlq-observer`,
/// `cloud-platform-infra` ADR-004). One <c>GetItem</c> by <c>MessageId</c> — the observer's Lambda
/// writes exactly one row per message, keyed the same way, so a single point lookup is sufficient;
/// no scan, no query.
/// </summary>
public sealed class DynamoDbObserverLogReader : IDlqObserverLogReader
{
    private readonly IAwsClientFactory _clientFactory;
    private readonly ILogger<DynamoDbObserverLogReader> _logger;

    /// <summary>Initialises a new instance of <see cref="DynamoDbObserverLogReader"/>.</summary>
    public DynamoDbObserverLogReader(IAwsClientFactory clientFactory, ILogger<DynamoDbObserverLogReader> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Aws;

    /// <inheritdoc />
    public async Task<bool> HasRecordedArrivalAsync(
        Namespace ns, string observerReference, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentException.ThrowIfNullOrWhiteSpace(observerReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        var client = _clientFactory.GetDynamoDbClient(ns);

        try
        {
            var response = await client.GetItemAsync(new GetItemRequest
            {
                TableName = observerReference,
                Key = new Dictionary<string, AttributeValue>
                {
                    ["MessageId"] = new AttributeValue { S = messageId },
                },
                // Only existence matters for a liveness check — never read BodyHash here.
                ProjectionExpression = "MessageId",
            }, cancellationToken).ConfigureAwait(false);

            return response.Item is { Count: > 0 };
        }
        catch (ResourceNotFoundException ex)
        {
            // The configured table doesn't exist — a misconfigured ObserverReference, not a
            // transient failure. Fail closed (not found) and log loudly so the operator's
            // attestation configuration gets fixed rather than silently staying dark.
            _logger.LogWarning(ex,
                "DLQ observer DynamoDB table {TableName} not found for namespace {NamespaceId} — " +
                "check DlqObserverAttestation.ObserverReference",
                LogRedactor.SanitiseForLog(observerReference), ns.Id);
            return false;
        }
    }
}
