using Amazon.Lambda;
using Amazon.Lambda.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using SqsMessage = Amazon.SQS.Model.Message;
using FluentAssertions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Providers.Aws;
using ServiceHub.Providers.Aws.Components;

namespace ServiceHub.UnitTests.Components.Sources;

/// <summary>What the stream-failures reader asks Lambda and SQS for, and what it promises never to do to a queue.</summary>
public sealed class AwsStreamsReaderTests
{
    private const string Queue = "arn:aws:sqs:us-east-1:123456789012:orders-failures";
    private const string QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456789012/orders-failures";
    private const string Function = "arn:aws:lambda:us-east-1:123456789012:function:process";
    private const string Kinesis = "arn:aws:kinesis:us-east-1:123456789012:stream/orders";
    private const string Dynamo = "arn:aws:dynamodb:us-east-1:123456789012:table/orders/stream/2026-10-01T00:00:00.000";

    private readonly Mock<IAmazonLambda> _lambda = new();
    private readonly Mock<IAmazonSQS> _sqs = new();
    private readonly AwsStreamsReader _reader;
    private readonly Namespace _ns = Namespace.Create(
        "sqs.us-east-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
        provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;

    public AwsStreamsReaderTests()
    {
        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetLambdaClient(It.IsAny<Namespace>())).Returns(_lambda.Object);
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(_sqs.Object);
        _reader = new AwsStreamsReader(factory.Object);
        _sqs.Setup(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(new GetQueueUrlResponse { QueueUrl = QueueUrl });
    }

    private static EventSourceMappingConfiguration Mapping(string uuid, string source, string? destination = Queue) => new()
    {
        UUID = uuid,
        EventSourceArn = source,
        FunctionArn = Function,
        DestinationConfig = destination is null ? null : new DestinationConfig { OnFailure = new OnFailure { Destination = destination } },
    };

    private static SqsMessage Msg(string id, long? sentMs = 1_790_000_000_000, string body = "{}")
    {
        var m = new SqsMessage { MessageId = id, Body = body };
        if (sentMs is { } ms)
        {
            m.Attributes["SentTimestamp"] = ms.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return m;
    }

    [Fact]
    public async Task Only_kinesis_and_dynamodb_stream_mappings_are_kept_and_their_failure_destination_is_read()
    {
        _lambda.Setup(l => l.ListEventSourceMappingsAsync(It.IsAny<ListEventSourceMappingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListEventSourceMappingsResponse
            {
                EventSourceMappings =
                [
                    Mapping("11111111-1111-1111-1111-111111111111", Kinesis),
                    Mapping("22222222-2222-2222-2222-222222222222", Dynamo, destination: null),
                    Mapping("33333333-3333-3333-3333-333333333333", "arn:aws:sqs:us-east-1:123456789012:work"),
                    Mapping("44444444-4444-4444-4444-444444444444", "arn:aws:dynamodb:us-east-1:123456789012:table/orders"),
                    Mapping("55555555-5555-5555-5555-555555555555", Kinesis + "/consumer/fan:123"),
                    new EventSourceMappingConfiguration { UUID = null, EventSourceArn = Kinesis, FunctionArn = Function },
                ],
            });

        var list = await _reader.ListMappingsAsync(_ns, default);

        list.Items.Select(m => m.Uuid).Should().Equal("11111111-1111-1111-1111-111111111111", "22222222-2222-2222-2222-222222222222", "55555555-5555-5555-5555-555555555555");
        list.Items[0].FailureDestinationArn.Should().Be(Queue);
        list.Items[1].FailureDestinationArn.Should().BeNull();
        list.More.Should().BeFalse();
    }

    [Fact]
    public async Task Mappings_are_read_page_by_page_until_the_marker_runs_out()
    {
        var calls = 0;
        _lambda.Setup(l => l.ListEventSourceMappingsAsync(It.IsAny<ListEventSourceMappingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ListEventSourceMappingsRequest r, CancellationToken _) =>
            {
                calls++;
                return r.Marker is null
                    ? new ListEventSourceMappingsResponse { EventSourceMappings = [Mapping("11111111-1111-1111-1111-111111111111", Kinesis)], NextMarker = "page2" }
                    : new ListEventSourceMappingsResponse { EventSourceMappings = [Mapping("22222222-2222-2222-2222-222222222222", Kinesis)] };
            });

        var list = await _reader.ListMappingsAsync(_ns, default);

        list.Items.Should().HaveCount(2);
        list.More.Should().BeFalse();
        calls.Should().Be(2);
    }

    [Fact]
    public async Task A_region_with_endless_pages_stops_at_the_page_cap_and_says_there_is_more()
    {
        var calls = 0;
        _lambda.Setup(l => l.ListEventSourceMappingsAsync(It.IsAny<ListEventSourceMappingsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return new ListEventSourceMappingsResponse { EventSourceMappings = [], NextMarker = "again" }; });

        var list = await _reader.ListMappingsAsync(_ns, default);

        calls.Should().Be(AwsStreamsReader.MaxMappingPages);
        list.More.Should().BeTrue();
    }

    [Fact]
    public async Task One_mapping_is_read_by_its_id_and_a_gone_one_is_null()
    {
        _lambda.Setup(l => l.GetEventSourceMappingAsync(It.Is<GetEventSourceMappingRequest>(r => r.UUID == "11111111-1111-1111-1111-111111111111"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetEventSourceMappingResponse
            {
                UUID = "11111111-1111-1111-1111-111111111111", EventSourceArn = Kinesis, FunctionArn = Function,
                DestinationConfig = new DestinationConfig { OnFailure = new OnFailure { Destination = Queue } },
            });
        _lambda.Setup(l => l.GetEventSourceMappingAsync(It.Is<GetEventSourceMappingRequest>(r => r.UUID == "gone"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Amazon.Lambda.Model.ResourceNotFoundException("no such mapping"));

        (await _reader.GetMappingAsync(_ns, "11111111-1111-1111-1111-111111111111", default))!.FailureDestinationArn.Should().Be(Queue);
        (await _reader.GetMappingAsync(_ns, "gone", default)).Should().BeNull();
    }

    [Fact]
    public async Task A_mapping_that_is_not_a_stream_is_null_even_when_it_exists()
    {
        _lambda.Setup(l => l.GetEventSourceMappingAsync(It.IsAny<GetEventSourceMappingRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetEventSourceMappingResponse { UUID = "u", EventSourceArn = "arn:aws:sqs:us-east-1:123456789012:work", FunctionArn = Function });

        (await _reader.GetMappingAsync(_ns, "u", default)).Should().BeNull();
    }

    [Fact]
    public async Task The_queue_size_is_visible_plus_in_flight_and_the_queue_is_found_by_name_and_owner()
    {
        _sqs.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse { Attributes = new Dictionary<string, string> { ["ApproximateNumberOfMessages"] = "7", ["ApproximateNumberOfMessagesNotVisible"] = "2" } });

        (await _reader.GetQueueDepthAsync(_ns, Queue, default)).Should().Be(9);

        _sqs.Verify(s => s.GetQueueUrlAsync(It.Is<GetQueueUrlRequest>(r => r.QueueName == "orders-failures" && r.QueueOwnerAWSAccountId == "123456789012"), It.IsAny<CancellationToken>()), Times.Once);
        _sqs.Verify(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never, "asking a size is never a receive");
    }

    [Fact]
    public async Task A_queue_that_does_not_exist_has_no_size_and_no_records()
    {
        _sqs.Setup(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>())).ThrowsAsync(new QueueDoesNotExistException("none"));

        (await _reader.GetQueueDepthAsync(_ns, Queue, default)).Should().BeNull();
        (await _reader.ListRecordsAsync(_ns, Queue, 10, default)).Items.Should().BeEmpty();
        (await _reader.GetRecordAsync(_ns, Queue, "m", default)).Should().BeNull();
    }

    [Theory]
    [InlineData("arn:aws:sqs:us-east-1:123456789012")]
    [InlineData("not-an-arn")]
    [InlineData("")]
    public async Task A_queue_arn_that_is_not_six_parts_is_never_turned_into_a_call(string arn)
    {
        (await _reader.GetQueueDepthAsync(_ns, arn, default)).Should().BeNull();
        (await _reader.ListRecordsAsync(_ns, arn, 10, default)).Items.Should().BeEmpty();

        _sqs.Verify(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_look_at_a_queue_is_a_receive_with_zero_visibility_that_keeps_the_message_and_asks_for_the_sent_time()
    {
        ReceiveMessageRequest? seen = null;
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback((ReceiveMessageRequest r, CancellationToken _) => seen ??= r)
            .ReturnsAsync(new ReceiveMessageResponse { Messages = [Msg("a", 1_790_000_000_000, "{\"x\":1}")] });

        var list = await _reader.ListRecordsAsync(_ns, Queue, 5, default);

        seen!.QueueUrl.Should().Be(QueueUrl);
        seen.VisibilityTimeout.Should().Be(0, "the message goes straight back to the queue");
        seen.MaxNumberOfMessages.Should().Be(AwsStreamsReader.ReceiveBatch);
        seen.WaitTimeSeconds.Should().Be(0);
        seen.MessageSystemAttributeNames.Should().Contain("SentTimestamp");
        list.Items.Single().Should().Be(new QueueRecord("a", DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_000), "{\"x\":1}"));
    }

    [Fact]
    public async Task A_look_never_deletes_sends_or_changes_visibility()
    {
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse { Messages = [Msg("a")] });

        await _reader.ListRecordsAsync(_ns, Queue, 5, default);
        await _reader.GetRecordAsync(_ns, Queue, "a", default);

        _sqs.Invocations.Select(i => i.Method.Name).Distinct()
            .Should().BeSubsetOf(["GetQueueUrlAsync", "ReceiveMessageAsync"], "the reader only finds the queue and looks at it");
    }

    [Fact]
    public async Task The_same_message_handed_out_twice_is_one_record_and_the_look_stops_when_nothing_new_arrives()
    {
        var calls = 0;
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return new ReceiveMessageResponse { Messages = [Msg("a"), Msg("b")] }; });

        var list = await _reader.ListRecordsAsync(_ns, Queue, 50, default);

        list.Items.Select(i => i.MessageId).Should().Equal("a", "b");
        calls.Should().Be(2, "the second receive brought nothing new, so the look stopped");
        list.More.Should().BeFalse();
    }

    [Fact]
    public async Task A_look_stops_at_the_requested_number_and_says_there_may_be_more()
    {
        var n = 0;
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ReceiveMessageResponse { Messages = [.. Enumerable.Range(0, 10).Select(_ => Msg($"m{n++}"))] });

        var list = await _reader.ListRecordsAsync(_ns, Queue, 15, default);

        list.Items.Should().HaveCount(15);
        list.More.Should().BeTrue();
    }

    [Fact]
    public async Task A_look_makes_no_more_receives_than_the_bound_even_if_new_messages_keep_arriving()
    {
        var n = 0;
        var calls = 0;
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return new ReceiveMessageResponse { Messages = [Msg($"m{n++}")] }; });

        await _reader.ListRecordsAsync(_ns, Queue, 1000, default);

        calls.Should().Be(AwsStreamsReader.MaxReceives);
    }

    [Fact]
    public async Task A_message_with_no_sent_time_has_no_time()
    {
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse { Messages = [Msg("a", sentMs: null)] });

        (await _reader.ListRecordsAsync(_ns, Queue, 5, default)).Items.Single().SentUtc.Should().BeNull();
    }

    [Fact]
    public async Task One_record_is_found_by_its_message_id_even_when_it_arrives_in_a_later_receive()
    {
        var calls = 0;
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return new ReceiveMessageResponse { Messages = [calls < 3 ? Msg($"other{calls}") : Msg("wanted", body: "BODY")] }; });

        var record = await _reader.GetRecordAsync(_ns, Queue, "wanted", default);

        record!.Body.Should().Be("BODY");
        calls.Should().Be(3);
    }

    [Fact]
    public async Task A_record_that_is_not_visible_is_null_and_the_search_stops_when_the_queue_is_empty()
    {
        var calls = 0;
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return new ReceiveMessageResponse { Messages = [] }; });

        (await _reader.GetRecordAsync(_ns, Queue, "wanted", default)).Should().BeNull();
        calls.Should().Be(1);
    }

    [Fact]
    public async Task The_search_for_a_record_is_bounded()
    {
        var calls = 0;
        _sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => { calls++; return new ReceiveMessageResponse { Messages = [Msg("never")] }; });

        (await _reader.GetRecordAsync(_ns, Queue, "wanted", default)).Should().BeNull();
        calls.Should().Be(AwsStreamsReader.MaxReceives);
    }

    [Fact]
    public async Task A_cancelled_caller_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _lambda.Setup(l => l.ListEventSourceMappingsAsync(It.IsAny<ListEventSourceMappingsRequest>(), It.IsAny<CancellationToken>()))
            .Returns((ListEventSourceMappingsRequest _, CancellationToken t) => Task.FromCanceled<ListEventSourceMappingsResponse>(t));

        await ((Func<Task>)(() => _reader.ListMappingsAsync(_ns, cts.Token))).Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public void A_missing_client_factory_is_refused()
    {
        ((Action)(() => _ = new AwsStreamsReader(null!))).Should().Throw<ArgumentNullException>();
    }
}
