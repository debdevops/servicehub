using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Core.Security;

namespace ServiceHub.Providers.Aws.Components;

/// <summary>
/// Kinesis and DynamoDB stream failure records (build-plan unit PP4c). <b>Read-only</b>. A stream keeps no failed copy: when Lambda gives up
/// on a batch it can write a small record, pointing at the batch (stream, shard, sequence numbers), to the mapping's on-failure queue.
/// This source reads those records. The original stream records expire and may already be gone, so every item says it is a pointer.
/// </summary>
/// <remarks>
/// Needs <c>lambda:ListEventSourceMappings</c>, <c>lambda:GetEventSourceMapping</c>, <c>sqs:GetQueueUrl</c>, <c>sqs:GetQueueAttributes</c> and
/// <c>sqs:ReceiveMessage</c>. Looking at a queue is a receive with a visibility timeout of zero: nothing is deleted or sent, but it counts
/// toward the message's receive count, so the on-failure queue must not carry a redrive policy. Only SQS destinations in the connection's
/// own region are read; an SNS destination, or one in another region, is listed as a place that cannot be read from here.
/// </remarks>
public sealed partial class StreamsSource : IComponentSource
{
    /// <summary>The catalogue component this serves.</summary>
    public const string Key = "aws-streams";

    private const int MaxTextLength = 1000;

    private readonly IAwsStreamsReader _reader;

    // The reader is this module's own seam to AWS, so a test can stand in for it; only the public constructor is for the host.
    internal StreamsSource(IAwsStreamsReader reader) =>
        _reader = reader ?? throw new ArgumentNullException(nameof(reader));

    /// <summary>Creates the source over AWS.</summary>
    public StreamsSource(IAwsClientFactory clients)
        : this(new AwsStreamsReader(clients))
    {
    }

    /// <inheritdoc />
    public string ComponentKey => Key;

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Aws;

    /// <inheritdoc />
    public ComponentSourceCapabilities Capabilities { get; } = new(ReasonInList: true, ReadsPayload: false);

    /// <inheritdoc />
    public async Task<Result> ValidateAccessAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        try
        {
            var mappings = await _reader.ListMappingsAsync(ns, ct).ConfigureAwait(false);

            // Listing mappings proves Lambda. If one names a queue, prove the queue can be looked at too (a size read, not a receive).
            var queue = mappings.Items.Select(m => m.FailureDestinationArn).FirstOrDefault(arn => IsReadableQueue(ns, arn));
            if (queue is not null)
            {
                await _reader.GetQueueDepthAsync(ns, queue, ct).ConfigureAwait(false);
            }

            return Result.Success();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // A cloud failure becomes a result the screen can show, never an exception.
        catch (Exception ex)
        {
            return Result.Failure(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<SourcePlacesPage>> ListPlacesAsync(Namespace ns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        try
        {
            var mappings = await _reader.ListMappingsAsync(ns, ct).ConfigureAwait(false);
            var ordered = mappings.Items.OrderBy(m => m.StreamArn, StringComparer.Ordinal).ThenBy(m => m.FunctionArn, StringComparer.Ordinal).ToList();
            return Result.Success(new SourcePlacesPage(
                [.. ordered.Take(ComponentSourceLimits.MaxPlaces).Select(m => new SourcePlace(m.Uuid, PlaceName(ns, m), "stream consumer", FunctionLink(ns, m.FunctionArn)))],
                mappings.More || ordered.Count > ComponentSourceLimits.MaxPlaces));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            return Result.Failure<SourcePlacesPage>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<FailedItemsPage>> ListFailedAsync(Namespace ns, string? placeId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        if (placeId is not null && !UuidRegex().IsMatch(placeId))
        {
            return Result.Failure<FailedItemsPage>(Error.Validation("AWS.Streams.InvalidPlace", "That is not a stream consumer."));
        }

        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
        budget.CancelAfter(ComponentSourceLimits.ListingBudget);
        try
        {
            List<StreamMapping> mappings;
            var incomplete = false;
            var reached = false;
            if (placeId is null)
            {
                var all = await _reader.ListMappingsAsync(ns, budget.Token).ConfigureAwait(false);
                mappings = [.. all.Items];
                reached = all.More;
            }
            else
            {
                var one = await _reader.GetMappingAsync(ns, placeId, budget.Token).ConfigureAwait(false);
                mappings = one is null ? [] : [one];
            }

            // A consumer with no on-failure queue keeps nothing. Asked for by name, or when no consumer keeps anything, that is said as an error:
            // an empty list would read as "nothing is wrong".
            if (mappings.Count > 0 && !mappings.Any(m => IsReadableQueue(ns, m.FailureDestinationArn)))
            {
                return Result.Failure<FailedItemsPage>(Error.Validation(
                    "AWS.Streams.NothingKept",
                    placeId is not null
                        ? "This consumer keeps nothing, or keeps it where ServiceHub cannot read: it has no on-failure SQS queue in this region. Add one to the event source mapping to see failures here."
                        : "No stream consumer keeps anything ServiceHub can read: none has an on-failure SQS queue in this region."));
            }

            // Two mappings can share one queue; it is looked at once.
            var queues = mappings.Select(m => m.FailureDestinationArn).Where(arn => IsReadableQueue(ns, arn)).Select(arn => arn!).Distinct(StringComparer.Ordinal).ToList();
            var results = await SourceReads.RunAsync(
                queues, (queue, token) => ReadQueueAsync(ns, queue, token), Fail, new Read([], false), budget.Token, ct).ConfigureAwait(false);

            incomplete |= results.Any(r => r.Failed);
            if (results.Count > 0 && results.All(r => r.Failed))
            {
                return Result.Failure<FailedItemsPage>(results.Select(r => r.Error).FirstOrDefault(e => e is not null) ?? Error.ExternalService("AWS.Streams.ReadFailed", "The on-failure queues could not be read."));
            }

            var byQueue = mappings.Where(m => IsReadableQueue(ns, m.FailureDestinationArn)).GroupBy(m => m.FailureDestinationArn!, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First());
            var items = new List<FailedItem>();
            for (var i = 0; i < queues.Count; i++)
            {
                var owner = byQueue[queues[i]];
                reached |= results[i].Value.More;
                items.AddRange(results[i].Value.Records.Select(r => ToItem(ns, owner, r)));
            }

            var ordered = items.OrderByDescending(i => i.AtUtc ?? DateTimeOffset.MinValue).ToList();
            reached |= ordered.Count > ComponentSourceLimits.MaxFailedItems;
            return Result.Success(new FailedItemsPage([.. ordered.Take(ComponentSourceLimits.MaxFailedItems)], reached, incomplete));
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result.Failure<FailedItemsPage>(Error.ExternalService("AWS.Streams.ReadTimedOut", "Reading the on-failure queues took too long. Try one stream consumer."));
        }
#pragma warning disable CA1031
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result.Failure<FailedItemsPage>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    /// <inheritdoc />
    public async Task<Result<FailedItemDetail?>> GetFailedAsync(Namespace ns, string itemId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentException.ThrowIfNullOrWhiteSpace(itemId);
        var m = itemId.Length <= 200 ? ItemRegex().Match(itemId) : Match.Empty;
        if (!m.Success)
        {
            return Result.Failure<FailedItemDetail?>(Error.Validation("AWS.Streams.InvalidItem", "That is not a stream failure record."));
        }

        try
        {
            var mapping = await _reader.GetMappingAsync(ns, m.Groups["u"].Value, ct).ConfigureAwait(false);
            if (mapping is null || !IsReadableQueue(ns, mapping.FailureDestinationArn))
            {
                return Result.Success<FailedItemDetail?>(null);
            }

            var record = await _reader.GetRecordAsync(ns, mapping.FailureDestinationArn!, m.Groups["m"].Value, ct).ConfigureAwait(false);
            if (record is null)
            {
                return Result.Success<FailedItemDetail?>(null);
            }

            var parsed = Parse(record.Body);
            return Result.Success<FailedItemDetail?>(new FailedItemDetail(
                ToItem(ns, mapping, record), Clean(parsed.Condition ?? parsed.FunctionError), Clean(parsed.Cause()), parsed.FirstArrivalUtc, parsed.LastArrivalUtc ?? record.SentUtc));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
        {
            return Result.Failure<FailedItemDetail?>(Fail(ex));
        }
#pragma warning restore CA1031
    }

    private sealed record Read(IReadOnlyList<QueueRecord> Records, bool More);

    private async Task<Read> ReadQueueAsync(Namespace ns, string queueArn, CancellationToken ct)
    {
        var listing = await _reader.ListRecordsAsync(ns, queueArn, ComponentSourceLimits.MaxFailedItems, ct).ConfigureAwait(false);

        // A look at a queue shows only what is visible now. If the queue says it holds more than was shown, say so.
        var depth = listing.Items.Count < ComponentSourceLimits.MaxFailedItems ? await _reader.GetQueueDepthAsync(ns, queueArn, ct).ConfigureAwait(false) : null;
        return new Read(listing.Items, listing.More || depth > listing.Items.Count);
    }

    /// <summary>What a failure record says, with nothing but the pointer fields: the batch's data is never in it.</summary>
    internal sealed record ParsedRecord(
        string? Condition, string? FunctionError, string? StreamArn, string? ShardId, string? StartSequence, string? EndSequence, int? BatchSize,
        DateTimeOffset? FirstArrivalUtc, DateTimeOffset? LastArrivalUtc, int? InvokeCount)
    {
        public string? Cause()
        {
            var parts = new List<string>();
            if (StreamArn is not null)
            {
                parts.Add($"stream {StreamName(StreamArn)}");
            }

            if (ShardId is not null)
            {
                parts.Add($"shard {ShardId}");
            }

            if (StartSequence is not null || EndSequence is not null)
            {
                parts.Add($"sequence {StartSequence ?? "?"} to {EndSequence ?? "?"}");
            }

            if (BatchSize is { } size)
            {
                parts.Add($"{size} record{(size == 1 ? string.Empty : "s")}");
            }

            if (InvokeCount is { } count)
            {
                parts.Add($"invoked {count} time{(count == 1 ? string.Empty : "s")}");
            }

            if (!string.IsNullOrWhiteSpace(FunctionError))
            {
                parts.Add($"function error {FunctionError}");
            }

            return parts.Count == 0 ? null : string.Join("; ", parts) + ". The original records may have expired.";
        }
    }

    /// <summary>Reads a failure record's pointer fields. A body that is not the expected shape reads as nothing, never as an error.</summary>
    internal static ParsedRecord Parse(string body)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var batch = Child(root, "KinesisBatchInfo");
            if (batch.ValueKind != JsonValueKind.Object)
            {
                batch = Child(root, "DDBStreamBatchInfo");
            }

            var request = Child(root, "requestContext");
            var response = Child(root, "responseContext");
            return new ParsedRecord(
                Text(request, "condition"), Text(response, "functionError"), Text(batch, "streamArn"), Text(batch, "shardId"),
                Text(batch, "startSequenceNumber"), Text(batch, "endSequenceNumber"), Int(batch, "batchSize"),
                Time(batch, "approximateArrivalOfFirstRecord"), Time(batch, "approximateArrivalOfLastRecord"), Int(request, "approximateInvokeCount"));
        }
        catch (JsonException)
        {
            return new ParsedRecord(null, null, null, null, null, null, null, null, null, null);
        }
    }

    private static FailedItem ToItem(Namespace ns, StreamMapping mapping, QueueRecord record)
    {
        var parsed = Parse(record.Body);
        var size = parsed.BatchSize is { } n ? $" of {n} record{(n == 1 ? string.Empty : "s")}" : string.Empty;
        var shard = parsed.ShardId is null ? string.Empty : $" from shard {Clean(parsed.ShardId)}";
        var why = parsed.Condition is null ? string.Empty : $" ({Clean(parsed.Condition)})";
        return new FailedItem(
            $"{mapping.Uuid}/{record.MessageId}", mapping.Uuid, parsed.LastArrivalUtc ?? record.SentUtc, "FAILED", Clean(parsed.Condition),
            $"Failed batch{size}{shard}{why}", CopyKind.Pointer, FunctionLink(ns, mapping.FunctionArn));
    }

    /// <summary>
    /// What people call the stream: the Kinesis stream's name (<c>arn:aws:kinesis:r:a:stream/orders</c>, or a consumer of it), or the DynamoDB
    /// table's name (<c>arn:aws:dynamodb:r:a:table/orders/stream/2026-10-01T00:00:00.000</c>). An ARN of neither shape is shown as it is.
    /// </summary>
    internal static string StreamName(string arn)
    {
        var kinesis = arn.IndexOf(":stream/", StringComparison.Ordinal);
        if (kinesis >= 0)
        {
            var name = arn[(kinesis + ":stream/".Length)..];
            var slash = name.IndexOf('/', StringComparison.Ordinal);
            return slash < 0 ? name : name[..slash];
        }

        var table = arn.IndexOf(":table/", StringComparison.Ordinal);
        if (table >= 0)
        {
            var name = arn[(table + ":table/".Length)..];
            var slash = name.IndexOf('/', StringComparison.Ordinal);
            return slash < 0 ? name : name[..slash];
        }

        return arn;
    }

    private static string PlaceName(Namespace ns, StreamMapping m)
    {
        var stream = StreamName(m.StreamArn);
        var function = m.FunctionArn[(m.FunctionArn.LastIndexOf(':') + 1)..];
        var note = m.FailureDestinationArn is null ? " (nothing is kept)"
            : IsReadableQueue(ns, m.FailureDestinationArn) ? string.Empty
            : " (on-failure destination cannot be read from here)";
        return $"{stream} → {function}{note}";
    }

    // arn:aws:sqs:{region}:{account}:{name}, in this connection's own region.
    private static bool IsReadableQueue(Namespace ns, string? arn)
    {
        var parts = arn?.Split(':');
        return parts is { Length: 6 } && parts[0] == "arn" && parts[1].StartsWith("aws", StringComparison.Ordinal) && parts[2] == "sqs"
            && string.Equals(parts[3], Region(ns), StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(parts[5]);
    }

    [GeneratedRegex(@"^[0-9a-fA-F-]{36}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex UuidRegex();

    [GeneratedRegex(@"^(?<u>[0-9a-fA-F-]{36})/(?<m>[A-Za-z0-9-]{1,100})$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex ItemRegex();

    private static JsonElement Child(JsonElement e, string name) => e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var p) ? p : default;

    private static string? Text(JsonElement e, string name) => Child(e, name) is { ValueKind: JsonValueKind.String } p ? p.GetString() : null;

    private static int? Int(JsonElement e, string name) => Child(e, name) is { ValueKind: JsonValueKind.Number } p && p.TryGetInt32(out var v) ? v : null;

    private static DateTimeOffset? Time(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Text(e, name), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.ToUniversalTime() : null;

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var safe = LogRedactor.SanitiseForLog(LogRedactor.Redact(text));
        return safe.Length <= MaxTextLength ? safe : safe[..MaxTextLength] + "…";
    }

    private static string Region(Namespace ns) => string.IsNullOrWhiteSpace(ns.AwsRegion) ? "us-east-1" : ns.AwsRegion.Trim();

    private static string FunctionLink(Namespace ns, string functionArn)
    {
        var region = Region(ns);
        var host = region.StartsWith("cn-", StringComparison.Ordinal) ? $"{region}.console.amazonaws.cn"
            : region.StartsWith("us-gov-", StringComparison.Ordinal) ? $"{region}.console.amazonaws-us-gov.com"
            : $"{region}.console.aws.amazon.com";
        return $"https://{host}/lambda/home?region={region}#/functions/{Uri.EscapeDataString(functionArn[(functionArn.LastIndexOf(':') + 1)..])}?tab=configure";
    }

    private static Error Fail(Exception ex) => ex is Amazon.Runtime.AmazonServiceException { ErrorCode: "AccessDeniedException" or "AccessDenied" or "UnrecognizedClientException" or "AWS.SimpleQueueService.AccessDenied" }
        ? Error.Forbidden("AWS.Streams.NotEnabled",
            "Not enabled for this connection: its credential may not read stream failures (needs lambda:ListEventSourceMappings, lambda:GetEventSourceMapping, sqs:GetQueueUrl, sqs:GetQueueAttributes and sqs:ReceiveMessage on the on-failure queue).")
        : Error.ExternalService("AWS.Streams.ReadFailed", LogRedactor.SanitiseForLog(LogRedactor.Redact(ex.Message)));
}
