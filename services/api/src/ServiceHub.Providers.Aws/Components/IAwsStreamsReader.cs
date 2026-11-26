using ServiceHub.Core.Entities;

namespace ServiceHub.Providers.Aws.Components;

/// <summary>A page and whether more follow it.</summary>
internal sealed record Listing<T>(IReadOnlyList<T> Items, bool More);

/// <summary>A Lambda event source mapping that reads a Kinesis or DynamoDB stream.</summary>
/// <param name="Uuid">The mapping's id.</param>
/// <param name="StreamArn">The stream it reads.</param>
/// <param name="FunctionArn">The function it feeds.</param>
/// <param name="FailureDestinationArn">The on-failure destination (an SQS queue or SNS topic), or null when the mapping keeps nothing.</param>
internal sealed record StreamMapping(string Uuid, string StreamArn, string FunctionArn, string? FailureDestinationArn);

/// <summary>One message in an on-failure queue: a failure record that points at a batch of the stream.</summary>
internal sealed record QueueRecord(string MessageId, DateTimeOffset? SentUtc, string Body);

/// <summary>
/// The only way <see cref="StreamsSource"/> touches AWS: list and get event source mappings, look at a queue's size, and look at its
/// messages. Narrow on purpose, so a test can stand in for AWS and the read-only promise is one small surface to read.
/// </summary>
internal interface IAwsStreamsReader
{
    /// <summary>The stream mappings of the connection's region, with or without an on-failure destination.</summary>
    Task<Listing<StreamMapping>> ListMappingsAsync(Namespace ns, CancellationToken ct);

    /// <summary>One mapping, or null when AWS no longer has it.</summary>
    Task<StreamMapping?> GetMappingAsync(Namespace ns, string uuid, CancellationToken ct);

    /// <summary>How many messages the queue holds, visible and in flight. Null when the queue does not exist.</summary>
    Task<int?> GetQueueDepthAsync(Namespace ns, string queueArn, CancellationToken ct);

    /// <summary>
    /// The messages that are visible in the queue now, up to <paramref name="max"/>, without deleting any. Looking at a queue is a
    /// receive: it counts toward the message's receive count, so the on-failure queue must not have a redrive policy.
    /// </summary>
    Task<Listing<QueueRecord>> ListRecordsAsync(Namespace ns, string queueArn, int max, CancellationToken ct);

    /// <summary>One visible message, or null when it is not visible now (gone, or in flight).</summary>
    Task<QueueRecord?> GetRecordAsync(Namespace ns, string queueArn, string messageId, CancellationToken ct);
}
