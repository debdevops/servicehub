using System.Globalization;
using Amazon.Lambda.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Aws.Components;

/// <summary>
/// Reads Lambda event source mappings and the on-failure queue they name. It calls <c>ListEventSourceMappings</c>,
/// <c>GetEventSourceMapping</c>, <c>GetQueueUrl</c>, <c>GetQueueAttributes</c> and <c>ReceiveMessage</c> with a visibility timeout of
/// zero, and never <c>DeleteMessage</c>, <c>SendMessage</c> or anything that changes visibility.
/// </summary>
internal sealed class AwsStreamsReader : IAwsStreamsReader
{
    /// <summary>The most pages of mappings read in one listing, so a region with very many functions is a bounded number of calls.</summary>
    internal const int MaxMappingPages = 5;

    /// <summary>SQS hands out at most this many messages in one receive.</summary>
    internal const int ReceiveBatch = 10;

    /// <summary>The most receives made in one look at a queue. A visible message can be handed out again, so a look stops when a receive brings nothing new.</summary>
    internal const int MaxReceives = 15;

    private readonly IAwsClientFactory _clients;

    public AwsStreamsReader(IAwsClientFactory clients) => _clients = clients ?? throw new ArgumentNullException(nameof(clients));

    public async Task<Listing<StreamMapping>> ListMappingsAsync(Namespace ns, CancellationToken ct)
    {
        var client = _clients.GetLambdaClient(ns);
        var found = new List<StreamMapping>();
        string? marker = null;
        var more = false;
        for (var page = 0; page < MaxMappingPages; page++)
        {
            var response = await client.ListEventSourceMappingsAsync(new ListEventSourceMappingsRequest { MaxItems = 100, Marker = marker }, ct).ConfigureAwait(false);
            found.AddRange(response.EventSourceMappings.Select(ToMapping).Where(m => m is not null).Select(m => m!));
            marker = response.NextMarker;
            if (string.IsNullOrEmpty(marker))
            {
                break;
            }

            more = page == MaxMappingPages - 1;
        }

        return new Listing<StreamMapping>(found, more);
    }

    public async Task<StreamMapping?> GetMappingAsync(Namespace ns, string uuid, CancellationToken ct)
    {
        try
        {
            var response = await _clients.GetLambdaClient(ns).GetEventSourceMappingAsync(new GetEventSourceMappingRequest { UUID = uuid }, ct).ConfigureAwait(false);
            return ToMapping(new EventSourceMappingConfiguration
            {
                UUID = response.UUID, EventSourceArn = response.EventSourceArn, FunctionArn = response.FunctionArn, DestinationConfig = response.DestinationConfig,
            });
        }
        catch (Amazon.Lambda.Model.ResourceNotFoundException)
        {
            return null;
        }
    }

    public async Task<int?> GetQueueDepthAsync(Namespace ns, string queueArn, CancellationToken ct)
    {
        var sqs = _clients.GetSqsClient(ns);
        var url = await QueueUrlAsync(sqs, queueArn, ct).ConfigureAwait(false);
        if (url is null)
        {
            return null;
        }

        var attributes = await sqs.GetQueueAttributesAsync(
            new GetQueueAttributesRequest { QueueUrl = url, AttributeNames = ["ApproximateNumberOfMessages", "ApproximateNumberOfMessagesNotVisible"] }, ct).ConfigureAwait(false);
        return attributes.ApproximateNumberOfMessages + attributes.ApproximateNumberOfMessagesNotVisible;
    }

    public async Task<Listing<QueueRecord>> ListRecordsAsync(Namespace ns, string queueArn, int max, CancellationToken ct)
    {
        var sqs = _clients.GetSqsClient(ns);
        var url = await QueueUrlAsync(sqs, queueArn, ct).ConfigureAwait(false);
        if (url is null)
        {
            return new Listing<QueueRecord>([], false);
        }

        var seen = new Dictionary<string, QueueRecord>(StringComparer.Ordinal);
        for (var receive = 0; receive < MaxReceives && seen.Count < max; receive++)
        {
            var added = 0;
            foreach (var message in await ReceiveAsync(sqs, url, ct).ConfigureAwait(false))
            {
                if (seen.Count < max && seen.TryAdd(message.MessageId, message))
                {
                    added++;
                }
            }

            if (added == 0)
            {
                break;
            }
        }

        return new Listing<QueueRecord>([.. seen.Values], seen.Count >= max);
    }

    public async Task<QueueRecord?> GetRecordAsync(Namespace ns, string queueArn, string messageId, CancellationToken ct)
    {
        var sqs = _clients.GetSqsClient(ns);
        var url = await QueueUrlAsync(sqs, queueArn, ct).ConfigureAwait(false);
        if (url is null)
        {
            return null;
        }

        for (var receive = 0; receive < MaxReceives; receive++)
        {
            var batch = await ReceiveAsync(sqs, url, ct).ConfigureAwait(false);
            if (batch.Count == 0)
            {
                break;
            }

            if (batch.FirstOrDefault(m => m.MessageId == messageId) is { } hit)
            {
                return hit;
            }
        }

        return null;
    }

    private static async Task<List<QueueRecord>> ReceiveAsync(IAmazonSQS sqs, string url, CancellationToken ct)
    {
        // VisibilityTimeout 0: the message is back in the queue the moment it is handed out, and nothing is deleted.
        var response = await sqs.ReceiveMessageAsync(
            new ReceiveMessageRequest
            {
                QueueUrl = url, MaxNumberOfMessages = ReceiveBatch, VisibilityTimeout = 0, WaitTimeSeconds = 0, MessageSystemAttributeNames = ["SentTimestamp"],
            }, ct).ConfigureAwait(false);
        return [.. response.Messages.Select(m => new QueueRecord(
            m.MessageId, m.Attributes.TryGetValue("SentTimestamp", out var sent) && long.TryParse(sent, CultureInfo.InvariantCulture, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : null, m.Body ?? string.Empty))];
    }

    private static async Task<string?> QueueUrlAsync(IAmazonSQS sqs, string queueArn, CancellationToken ct)
    {
        // arn:aws:sqs:{region}:{account}:{name}
        var parts = queueArn.Split(':');
        if (parts.Length != 6)
        {
            return null;
        }

        try
        {
            return (await sqs.GetQueueUrlAsync(new GetQueueUrlRequest { QueueName = parts[5], QueueOwnerAWSAccountId = parts[4] }, ct).ConfigureAwait(false)).QueueUrl;
        }
        catch (QueueDoesNotExistException)
        {
            return null;
        }
    }

    private static StreamMapping? ToMapping(EventSourceMappingConfiguration m) =>
        m.UUID is not null && m.EventSourceArn is not null && m.FunctionArn is not null && IsStream(m.EventSourceArn)
            ? new StreamMapping(m.UUID, m.EventSourceArn, m.FunctionArn, m.DestinationConfig?.OnFailure?.Destination)
            : null;

    // arn:aws:kinesis:{region}:{account}:stream/{name}[/consumer/...]  or  arn:aws:dynamodb:{region}:{account}:table/{table}/stream/{label}
    private static bool IsStream(string arn) =>
        (arn.Contains(":kinesis:", StringComparison.Ordinal) && arn.Contains(":stream/", StringComparison.Ordinal))
        || (arn.Contains(":dynamodb:", StringComparison.Ordinal) && arn.Contains("/stream/", StringComparison.Ordinal));
}
