using System.Net;
using Amazon.Lambda;
using Amazon.Runtime;
using FluentAssertions;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Aws.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>The stream-failures source against a fake AWS: the conformance kit plus what is particular to this service.</summary>
public sealed class StreamsSourceTests : ComponentSourceConformance
{
    private const string Uuid = "11111111-1111-1111-1111-111111111111";
    private const string Uuid2 = "22222222-2222-2222-2222-222222222222";
    private const string Kinesis = "arn:aws:kinesis:us-east-1:123456789012:stream/orders";
    private const string Function = "arn:aws:lambda:us-east-1:123456789012:function:process";
    private const string Queue = "arn:aws:sqs:us-east-1:123456789012:orders-failures";

    private readonly FakeAws _aws = new();
    private readonly StreamsSource _source;
    private readonly Namespace _ns = Namespace.Create(
        "sqs.us-east-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
        provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;

    public StreamsSourceTests()
    {
        _source = new StreamsSource(_aws);
        _aws.Mappings = [new StreamMapping(Uuid, Kinesis, Function, Queue)];
    }

    protected override IComponentSource Source => _source;

    protected override Namespace Connection => _ns;

    protected override string PlaceId => Uuid;

    protected override string ItemId => $"{Uuid}/msg-0";

    protected override IReadOnlyList<string> CloudCalls => _aws.Calls;

    protected override void ArrangeFailures(int count)
    {
        var baseTime = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _aws.Records[Queue] = [.. Enumerable.Range(0, count).Select(i => new QueueRecord($"msg-{i}", baseTime.AddMinutes(i), KinesisBody(baseTime.AddMinutes(i))))];
    }

    protected override void ArrangeAccessDenied() => _aws.Throw = new AmazonLambdaException("denied", ErrorType.Sender, "AccessDeniedException", "req", HttpStatusCode.Forbidden);

    protected override void ArrangeCloudFailure(string message) => _aws.Throw = new InvalidOperationException(message);

    protected override void ArrangeSecretInFailureText(string secret)
    {
        var at = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _aws.Records[Queue] =
        [
            new QueueRecord("msg-0", at, "{\"requestContext\":{\"condition\":\"" + secret + "\"},\"responseContext\":{\"functionError\":\"" + secret + "\"},\"KinesisBatchInfo\":{\"shardId\":\"" + secret + "\"}}"),
        ];
    }

    protected override void ArrangeMissingItem() => _aws.Records[Queue] = [];

    protected override void ArrangeCancellationAware() => _aws.HonourCancellation = true;

    private static string KinesisBody(DateTimeOffset last) =>
        "{\"requestContext\":{\"requestId\":\"r\",\"functionArn\":\"" + Function + "\",\"condition\":\"RetryAttemptsExhausted\",\"approximateInvokeCount\":3}," +
        "\"responseContext\":{\"statusCode\":200,\"functionError\":\"Unhandled\"},\"version\":\"1.0\",\"timestamp\":\"" + last.ToString("O") + "\"," +
        "\"KinesisBatchInfo\":{\"shardId\":\"shardId-000000000001\",\"startSequenceNumber\":\"4959\",\"endSequenceNumber\":\"4961\"," +
        "\"approximateArrivalOfFirstRecord\":\"" + last.AddSeconds(-5).ToString("O") + "\",\"approximateArrivalOfLastRecord\":\"" + last.ToString("O") + "\"," +
        "\"batchSize\":3,\"streamArn\":\"" + Kinesis + "\"}}";

    [Fact]
    public async Task Places_are_the_stream_consumers_named_by_stream_and_function_and_sorted()
    {
        _aws.Mappings =
        [
            new StreamMapping(Uuid2, "arn:aws:dynamodb:us-east-1:123456789012:table/t/stream/2026", "arn:aws:lambda:us-east-1:123456789012:function:audit", Queue),
            new StreamMapping(Uuid, Kinesis, Function, Queue),
        ];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Select(p => p.Id).Should().Equal(Uuid2, Uuid);
        places.Items.Select(p => p.Name).Should().Equal("t → audit", "orders → process");
        places.Items.Should().OnlyContain(p => p.Kind == "stream consumer");
        places.Items[1].ConsoleUrl.Should().Be("https://us-east-1.console.aws.amazon.com/lambda/home?region=us-east-1#/functions/process?tab=configure");
    }

    [Fact]
    public async Task A_consumer_with_no_on_failure_queue_is_listed_and_says_nothing_is_kept()
    {
        _aws.Mappings = [new StreamMapping(Uuid, Kinesis, Function, null)];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Single().Name.Should().Be("orders → process (nothing is kept)");
    }

    [Theory]
    [InlineData("arn:aws:sns:us-east-1:123456789012:failures")]
    [InlineData("arn:aws:sqs:eu-west-1:123456789012:other-region")]
    [InlineData("arn:aws:sqs:us-east-1:123456789012")]
    [InlineData("not-an-arn")]
    public async Task A_destination_that_cannot_be_read_from_here_is_said_so_in_the_place_name(string destination)
    {
        _aws.Mappings = [new StreamMapping(Uuid, Kinesis, Function, destination)];

        var places = (await _source.ListPlacesAsync(_ns, default)).Value;

        places.Items.Single().Name.Should().Contain("cannot be read from here");
    }

    [Fact]
    public async Task A_consumer_that_keeps_nothing_is_an_error_when_asked_for_by_name_not_an_empty_list()
    {
        _aws.Mappings = [new StreamMapping(Uuid, Kinesis, Function, null)];

        var page = await _source.ListFailedAsync(_ns, Uuid, default);

        page.IsFailure.Should().BeTrue();
        page.Error.Message.Should().Contain("keeps nothing");
        _aws.Calls.Should().NotContain("ListRecords", "a queue that is not there is never looked at");
    }

    [Fact]
    public async Task When_no_consumer_keeps_anything_the_whole_listing_is_an_error()
    {
        _aws.Mappings = [new StreamMapping(Uuid, Kinesis, Function, null), new StreamMapping(Uuid2, Kinesis, Function, "arn:aws:sns:us-east-1:123456789012:t")];

        var page = await _source.ListFailedAsync(_ns, null, default);

        page.IsFailure.Should().BeTrue();
        page.Error.Message.Should().Contain("No stream consumer keeps anything");
    }

    [Fact]
    public async Task No_consumers_at_all_is_an_empty_page_not_an_error()
    {
        _aws.Mappings = [];

        var page = await _source.ListFailedAsync(_ns, null, default);

        page.IsSuccess.Should().BeTrue();
        page.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task One_consumer_that_keeps_something_is_enough_to_list_without_an_error()
    {
        ArrangeFailures(2);
        _aws.Mappings = [new StreamMapping(Uuid, Kinesis, Function, Queue), new StreamMapping(Uuid2, Kinesis, Function, null)];

        var page = (await _source.ListFailedAsync(_ns, null, default)).Value;

        page.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task Two_consumers_that_share_a_queue_look_at_it_once()
    {
        ArrangeFailures(2);
        _aws.Mappings = [new StreamMapping(Uuid, Kinesis, Function, Queue), new StreamMapping(Uuid2, Kinesis, Function, Queue)];

        var page = (await _source.ListFailedAsync(_ns, null, default)).Value;

        _aws.Calls.Count(c => c == "ListRecords").Should().Be(1);
        page.Items.Should().HaveCount(2, "a record is not listed once per consumer");
    }

    [Fact]
    public async Task A_record_is_a_pointer_to_the_batch_with_the_condition_as_its_reason_code()
    {
        ArrangeFailures(1);

        var item = (await _source.ListFailedAsync(_ns, Uuid, default)).Value.Items.Single();

        item.Id.Should().Be($"{Uuid}/msg-0");
        item.Place.Should().Be(Uuid);
        item.Status.Should().Be("FAILED");
        item.ReasonCode.Should().Be("RetryAttemptsExhausted");
        item.Summary.Should().Be("Failed batch of 3 records from shard shardId-000000000001 (RetryAttemptsExhausted)");
        item.Copy.Should().Be(CopyKind.Pointer);
        item.AtUtc.Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task A_body_that_is_not_a_failure_record_is_still_listed_with_what_the_queue_knows()
    {
        var sent = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        _aws.Records[Queue] = [new QueueRecord("msg-0", sent, "plain text, not json")];

        var item = (await _source.ListFailedAsync(_ns, Uuid, default)).Value.Items.Single();

        item.Summary.Should().Be("Failed batch");
        item.ReasonCode.Should().BeNull();
        item.AtUtc.Should().Be(sent, "the queue's own sent time is used when the record gives none");
    }

    [Fact]
    public async Task A_queue_that_holds_more_than_was_shown_says_so()
    {
        ArrangeFailures(3);
        _aws.Depth[Queue] = 40;

        var page = (await _source.ListFailedAsync(_ns, Uuid, default)).Value;

        page.Items.Should().HaveCount(3);
        page.ReachedLimit.Should().BeTrue("the queue says it holds 40 and only 3 are visible");
    }

    [Fact]
    public async Task A_queue_that_holds_no_more_than_was_shown_does_not_claim_more()
    {
        ArrangeFailures(3);
        _aws.Depth[Queue] = 3;

        (await _source.ListFailedAsync(_ns, Uuid, default)).Value.ReachedLimit.Should().BeFalse();
    }

    [Fact]
    public async Task A_queue_whose_size_is_unknown_does_not_claim_more()
    {
        ArrangeFailures(3);
        _aws.Depth[Queue] = null;

        (await _source.ListFailedAsync(_ns, Uuid, default)).Value.ReachedLimit.Should().BeFalse();
    }

    [Fact]
    public async Task The_size_is_not_asked_when_the_cap_was_already_reached()
    {
        ArrangeFailures(ComponentSourceLimits.MaxFailedItems + 10);

        await _source.ListFailedAsync(_ns, Uuid, default);

        _aws.Calls.Should().NotContain("GetQueueDepth");
    }

    [Fact]
    public async Task One_queue_that_cannot_be_read_leaves_the_page_incomplete_and_keeps_the_other()
    {
        ArrangeFailures(2);
        const string Broken = "arn:aws:sqs:us-east-1:123456789012:broken";
        _aws.Mappings = [new StreamMapping(Uuid, Kinesis, Function, Queue), new StreamMapping(Uuid2, Kinesis, Function, Broken)];
        _aws.FailQueue = Broken;

        var page = (await _source.ListFailedAsync(_ns, null, default)).Value;

        page.Items.Should().HaveCount(2);
        page.Incomplete.Should().BeTrue();
    }

    [Fact]
    public async Task When_every_queue_fails_the_listing_is_an_error()
    {
        _aws.FailQueue = Queue;

        (await _source.ListFailedAsync(_ns, Uuid, default)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task A_consumer_that_is_gone_gives_an_empty_page()
    {
        _aws.Mappings = [];

        var page = await _source.ListFailedAsync(_ns, Uuid, default);

        page.IsSuccess.Should().BeTrue();
        page.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task The_detail_names_the_condition_and_puts_the_batch_pointer_in_the_cause()
    {
        ArrangeFailures(1);

        var detail = (await _source.GetFailedAsync(_ns, ItemId, default)).Value!;

        detail.Reason.Should().Be("RetryAttemptsExhausted");
        detail.Cause.Should().Be("stream orders; shard shardId-000000000001; sequence 4959 to 4961; 3 records; invoked 3 times; function error Unhandled. The original records may have expired.");
        detail.StartedUtc.Should().NotBeNull();
        detail.StoppedUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task The_reason_falls_back_to_the_function_error_when_there_is_no_condition()
    {
        _aws.Records[Queue] = [new QueueRecord("msg-0", null, "{\"responseContext\":{\"functionError\":\"Unhandled\"}}")];

        var detail = (await _source.GetFailedAsync(_ns, ItemId, default)).Value!;

        detail.Reason.Should().Be("Unhandled");
    }

    [Fact]
    public async Task A_dynamodb_stream_record_is_read_the_same_way()
    {
        _aws.Records[Queue] = [new QueueRecord("msg-0", null,
            "{\"requestContext\":{\"condition\":\"RetryAttemptsExhausted\"},\"DDBStreamBatchInfo\":{\"shardId\":\"shard-1\",\"startSequenceNumber\":\"100\",\"endSequenceNumber\":\"200\",\"batchSize\":1,\"streamArn\":\"arn:aws:dynamodb:us-east-1:123456789012:table/orders/stream/2026-10-01\"}}")];

        var detail = (await _source.GetFailedAsync(_ns, ItemId, default)).Value!;

        detail.Cause.Should().Contain("stream orders").And.Contain("shard shard-1").And.Contain("1 record.");
    }

    [Fact]
    public async Task A_record_that_is_no_longer_visible_is_a_success_with_nothing()
    {
        ArrangeFailures(1);
        _aws.Records[Queue] = [];

        (await _source.GetFailedAsync(_ns, ItemId, default)).Value.Should().BeNull();
    }

    [Fact]
    public async Task A_consumer_gone_or_without_a_queue_gives_nothing_for_an_item()
    {
        _aws.Mappings = [new StreamMapping(Uuid, Kinesis, Function, null)];
        (await _source.GetFailedAsync(_ns, ItemId, default)).Value.Should().BeNull();

        _aws.Mappings = [];
        (await _source.GetFailedAsync(_ns, ItemId, default)).Value.Should().BeNull();
    }

    [Fact]
    public async Task Access_is_proved_by_reading_a_size_never_by_receiving_a_message()
    {
        var access = await _source.ValidateAccessAsync(_ns, default);

        access.IsSuccess.Should().BeTrue();
        _aws.Calls.Should().Equal("ListMappings", "GetQueueDepth");
    }

    [Fact]
    public async Task Access_succeeds_with_no_consumers_and_makes_no_queue_call()
    {
        _aws.Mappings = [];

        (await _source.ValidateAccessAsync(_ns, default)).IsSuccess.Should().BeTrue();
        _aws.Calls.Should().Equal("ListMappings");
    }

    [Fact]
    public async Task Access_skips_the_queue_check_for_a_destination_that_is_not_readable_here()
    {
        _aws.Mappings = [new StreamMapping(Uuid, Kinesis, Function, "arn:aws:sns:us-east-1:123456789012:t")];

        (await _source.ValidateAccessAsync(_ns, default)).IsSuccess.Should().BeTrue();
        _aws.Calls.Should().Equal("ListMappings");
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-uuid")]
    [InlineData("11111111-1111-1111-1111-11111111111")]
    [InlineData("11111111-1111-1111-1111-1111111111111")]
    [InlineData("11111111-1111-1111-1111-11111111111g")]
    [InlineData("arn:aws:lambda:us-east-1:123456789012:function:process")]
    public async Task A_place_that_is_not_a_mapping_id_is_refused_before_any_call(string place)
    {
        var page = await _source.ListFailedAsync(_ns, place, default);

        page.IsFailure.Should().BeTrue();
        _aws.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("11111111-1111-1111-1111-111111111111")]
    [InlineData("11111111-1111-1111-1111-111111111111/")]
    [InlineData("11111111-1111-1111-1111-111111111111/a/b")]
    [InlineData("11111111-1111-1111-1111-111111111111/a b")]
    [InlineData("11111111-1111-1111-1111-111111111111/../x")]
    [InlineData("/msg-0")]
    public async Task An_item_that_is_not_a_record_is_refused_before_any_call(string item)
    {
        var detail = await _source.GetFailedAsync(_ns, item, default);

        detail.IsFailure.Should().BeTrue();
        _aws.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_queue_in_the_govcloud_partition_of_this_region_is_readable()
    {
        var gov = Namespace.Create("sqs.us-gov-west-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY", provider: CloudProviderType.Aws, awsRegion: "us-gov-west-1").Value;
        _aws.Mappings = [new StreamMapping(Uuid, "arn:aws-us-gov:kinesis:us-gov-west-1:123456789012:stream/orders", "arn:aws-us-gov:lambda:us-gov-west-1:123456789012:function:process", "arn:aws-us-gov:sqs:us-gov-west-1:123456789012:q")];

        var places = (await _source.ListPlacesAsync(gov, default)).Value;

        places.Items.Single().Name.Should().Be("orders → process");
        places.Items.Single().ConsoleUrl.Should().StartWith("https://us-gov-west-1.console.amazonaws-us-gov.com/");
    }

    [Fact]
    public async Task A_listing_that_runs_out_of_time_is_a_failure_that_says_so_and_a_places_read_passes_the_cancellation_on()
    {
        _aws.Throw = new OperationCanceledException();

        var failed = await _source.ListFailedAsync(_ns, null, default);

        failed.IsFailure.Should().BeTrue();
        failed.Error.Message.Should().Contain("took too long");
        await ((Func<Task>)(() => _source.ListPlacesAsync(_ns, default))).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => _source.ValidateAccessAsync(_ns, default))).Should().ThrowAsync<OperationCanceledException>();
        await ((Func<Task>)(() => _source.GetFailedAsync(_ns, ItemId, default))).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task A_read_of_one_consumer_that_runs_out_of_time_is_a_failure_not_an_empty_list()
    {
        _aws.Throw = new OperationCanceledException();

        (await _source.ListFailedAsync(_ns, Uuid, default)).IsFailure.Should().BeTrue();
    }

    [Fact]
    public void It_names_the_aws_adapter_and_declares_a_reason_but_no_payload()
    {
        _source.Provider.Should().Be(CloudProviderType.Aws);
        _source.ComponentKey.Should().Be("aws-streams");
        _source.Capabilities.ReasonInList.Should().BeTrue();
        _source.Capabilities.ReadsPayload.Should().BeFalse();
    }

    [Theory]
    [InlineData("{\"requestContext\":{\"condition\":\"RetryAttemptsExhausted\",\"approximateInvokeCount\":2}}", "RetryAttemptsExhausted", 2)]
    [InlineData("{\"requestContext\":{}}", null, null)]
    [InlineData("[]", null, null)]
    [InlineData("\"text\"", null, null)]
    [InlineData("", null, null)]
    [InlineData("{not json", null, null)]
    public void The_parser_reads_the_pointer_fields_and_never_throws(string body, string? condition, int? invokes)
    {
        var parsed = StreamsSource.Parse(body);

        parsed.Condition.Should().Be(condition);
        parsed.InvokeCount.Should().Be(invokes);
    }

    [Fact]
    public void The_parser_reads_a_kinesis_batch()
    {
        var parsed = StreamsSource.Parse(KinesisBody(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)));

        parsed.ShardId.Should().Be("shardId-000000000001");
        parsed.StartSequence.Should().Be("4959");
        parsed.EndSequence.Should().Be("4961");
        parsed.BatchSize.Should().Be(3);
        parsed.StreamArn.Should().Be(Kinesis);
        parsed.FunctionError.Should().Be("Unhandled");
        parsed.LastArrivalUtc.Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero));
        parsed.FirstArrivalUtc.Should().Be(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero).AddSeconds(-5));
    }

    [Fact]
    public void A_cause_with_one_record_and_one_invoke_is_singular_and_an_empty_one_is_null()
    {
        new StreamsSource.ParsedRecord(null, null, null, null, null, null, 1, null, null, 1).Cause().Should().Be("1 record; invoked 1 time. The original records may have expired.");
        new StreamsSource.ParsedRecord(null, null, null, null, null, null, null, null, null, null).Cause().Should().BeNull();
    }

    /// <summary>AWS as the source sees it: records the operation names and answers from what a test arranged.</summary>
    private sealed class FakeAws : IAwsStreamsReader
    {
        private readonly List<string> _calls = [];

        public IReadOnlyList<string> Calls => _calls;

        public IReadOnlyList<StreamMapping> Mappings { get; set; } = [];

        public Dictionary<string, IReadOnlyList<QueueRecord>> Records { get; } = [];

        public Dictionary<string, int?> Depth { get; } = [];

        public Exception? Throw { get; set; }

        public bool HonourCancellation { get; set; }

        public string? FailQueue { get; set; }

        public Task<Listing<StreamMapping>> ListMappingsAsync(Namespace ns, CancellationToken ct) =>
            Run("ListMappings", ct, () => new Listing<StreamMapping>(Mappings, false));

        public Task<StreamMapping?> GetMappingAsync(Namespace ns, string uuid, CancellationToken ct) =>
            Run("GetMapping", ct, () => Mappings.FirstOrDefault(m => m.Uuid == uuid));

        public Task<int?> GetQueueDepthAsync(Namespace ns, string queueArn, CancellationToken ct) =>
            Run("GetQueueDepth", ct, () => Depth.TryGetValue(queueArn, out var d) ? d : Records.TryGetValue(queueArn, out var r) ? r.Count : 0);

        public Task<Listing<QueueRecord>> ListRecordsAsync(Namespace ns, string queueArn, int max, CancellationToken ct) =>
            Run("ListRecords", ct, () =>
            {
                if (queueArn == FailQueue)
                {
                    throw new InvalidOperationException("queue down");
                }

                var all = (Records.TryGetValue(queueArn, out var r) ? r : []).ToList();
                return new Listing<QueueRecord>([.. all.Take(max)], all.Count > max);
            });

        public Task<QueueRecord?> GetRecordAsync(Namespace ns, string queueArn, string messageId, CancellationToken ct) =>
            Run("GetRecord", ct, () => (Records.TryGetValue(queueArn, out var r) ? r : []).FirstOrDefault(m => m.MessageId == messageId));

        private Task<T> Run<T>(string call, CancellationToken ct, Func<T> answer)
        {
            lock (_calls)
            {
                _calls.Add(call);
            }

            if (HonourCancellation)
            {
                ct.ThrowIfCancellationRequested();
            }

            if (Throw is not null)
            {
                return Task.FromException<T>(Throw);
            }

            try
            {
                return Task.FromResult(answer());
            }
            catch (Exception ex)
            {
                return Task.FromException<T>(ex);
            }
        }
    }
}
