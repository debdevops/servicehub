using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Providers.Aws;
using ServiceHub.Providers.Aws.DlqObserver;
using Message = Amazon.SQS.Model.Message;

namespace ServiceHub.UnitTests.Providers.Aws;

/// <summary>
/// ADR-0018: a whole-queue scan either PROVES it saw every message or says it did not. One scan that says "complete" and is
/// wrong would let a failed replay be called verified — so every way a scan can miss something must end as "incomplete".
/// </summary>
public sealed class SqsWholeQueueScanTests
{
    private const string Dlq = "https://sqs.test/1/orders-dlq";

    /// <summary>A dead-letter queue that behaves as SQS does: a receive hides a message until it is released.</summary>
    private sealed class FakeQueue
    {
        public readonly List<Message> Messages = [];
        public readonly HashSet<string> Held = [];
        public readonly HashSet<string> NeverHandedOut = [];
        public bool Fifo;
        public bool Redrive;
        public int HeldByAnotherReader;
        public int Receives;
        public int Releases;

        public FakeQueue(int count, string? marker = null, int markerAt = -1)
        {
            for (var i = 0; i < count; i++)
            {
                var m = new Message { MessageId = $"m-{i}", ReceiptHandle = $"h-{i}", MessageAttributes = [] };
                if (i == markerAt && marker is not null)
                {
                    m.MessageAttributes["x-servicehub-recovery-id"] = new MessageAttributeValue { DataType = "String", StringValue = marker };
                }

                Messages.Add(m);
            }
        }

        public IAmazonSQS Client()
        {
            var sqs = new Mock<IAmazonSQS>();
            sqs.Setup(s => s.GetQueueAttributesAsync(It.Is<GetQueueAttributesRequest>(r => r.QueueUrl == Dlq), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    var attributes = new Dictionary<string, string>
                    {
                        ["ApproximateNumberOfMessages"] = (Messages.Count - Held.Count).ToString(),
                        ["ApproximateNumberOfMessagesNotVisible"] = (Held.Count + HeldByAnotherReader).ToString(),
                    };
                    if (Fifo) attributes["FifoQueue"] = "true";
                    if (Redrive) attributes["RedrivePolicy"] = """{"deadLetterTargetArn":"arn:aws:sqs:eu-west-1:1:dlq2","maxReceiveCount":3}""";
                    return new GetQueueAttributesResponse { Attributes = attributes };
                });
            sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(() =>
                {
                    Receives++;
                    var batch = Messages.Where(m => !Held.Contains(m.MessageId) && !NeverHandedOut.Contains(m.MessageId)).Take(10).ToList();
                    batch.ForEach(m => Held.Add(m.MessageId));
                    return new ReceiveMessageResponse { Messages = batch };
                });
            sqs.Setup(s => s.ChangeMessageVisibilityBatchAsync(It.IsAny<ChangeMessageVisibilityBatchRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((ChangeMessageVisibilityBatchRequest r, CancellationToken _) =>
                {
                    foreach (var e in r.Entries)
                    {
                        Releases++;
                        Held.Remove(Messages.First(m => m.ReceiptHandle == e.ReceiptHandle).MessageId);
                    }

                    return new ChangeMessageVisibilityBatchResponse();
                });
            return sqs.Object;
        }
    }

    private static SqsWholeQueueScanner Scanner(TimeProvider? time = null) => new(time, (_, _) => Task.CompletedTask);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(1000)]
    public async Task It_lists_every_message_and_gives_every_one_back(int count)
    {
        var queue = new FakeQueue(count);

        var scan = await Scanner().ScanAsync(queue.Client(), Dlq, CancellationToken.None);

        scan.Complete.Should().BeTrue();
        scan.Messages.Select(m => m.MessageId).Should().BeEquivalentTo(queue.Messages.Select(m => m.MessageId));
        queue.Held.Should().BeEmpty("a scan consumes nothing");
        queue.Releases.Should().Be(count);
    }

    [Fact]
    public async Task It_reads_the_replay_marker_so_a_returned_replay_can_be_recognised()
    {
        var scan = await Scanner().ScanAsync(new FakeQueue(12, marker: "entry-7", markerAt: 5).Client(), Dlq, CancellationToken.None);

        scan.Complete.Should().BeTrue();
        scan.Messages.Single(m => m.MessageId == "m-5").RecoveryMarker.Should().Be("entry-7");
        scan.Messages.Count(m => m.RecoveryMarker is null).Should().Be(11);
    }

    [Fact]
    public async Task A_message_that_SQS_does_not_hand_out_makes_the_scan_incomplete_never_a_wrong_complete()
    {
        // The case AwsMessageReceiver records from a real queue: a message that is there but is not returned.
        var queue = new FakeQueue(40);
        queue.NeverHandedOut.Add("m-17");

        var scan = await Scanner().ScanAsync(queue.Client(), Dlq, CancellationToken.None);

        scan.Complete.Should().BeFalse();
        scan.Reason.Should().Be("COUNT_MISMATCH");
        scan.Messages.Should().BeEmpty("an incomplete scan must not be mistaken for a list of everything");
        queue.Held.Should().BeEmpty("what was taken is still given back");
    }

    [Fact]
    public async Task A_FIFO_queue_is_never_scanned()
    {
        var queue = new FakeQueue(3) { Fifo = true };
        var scan = await Scanner().ScanAsync(queue.Client(), Dlq, CancellationToken.None);
        scan.Should().BeEquivalentTo(WholeQueueScan.Incomplete("FIFO_QUEUE"));
        queue.Receives.Should().Be(0);
    }

    [Fact]
    public async Task A_dead_letter_queue_with_its_own_redrive_policy_is_never_scanned()
    {
        // There a receive count is real: scanning could dead-letter a message again.
        var queue = new FakeQueue(3) { Redrive = true };
        var scan = await Scanner().ScanAsync(queue.Client(), Dlq, CancellationToken.None);
        scan.Reason.Should().Be("DLQ_HAS_REDRIVE");
        queue.Receives.Should().Be(0);
    }

    [Fact]
    public async Task Another_reader_holding_messages_makes_it_incomplete()
    {
        var queue = new FakeQueue(5) { HeldByAnotherReader = 2 };
        var scan = await Scanner().ScanAsync(queue.Client(), Dlq, CancellationToken.None);
        scan.Reason.Should().Be("OTHER_READER");
        queue.Receives.Should().Be(0);
    }

    [Fact]
    public async Task More_than_the_limit_is_incomplete()
    {
        var scan = await Scanner().ScanAsync(new FakeQueue(SqsWholeQueueScanner.MaxMessages + 1).Client(), Dlq, CancellationToken.None);
        scan.Reason.Should().Be("TOO_MANY");
    }

    [Fact]
    public async Task A_complete_scan_is_reused_for_a_minute_and_an_incomplete_one_never_is()
    {
        var time = new FakeTimeProvider(DateTimeOffset.Parse("2026-10-04T12:00:00Z"));
        var scanner = Scanner(time);
        var queue = new FakeQueue(20);
        var sqs = queue.Client();

        await scanner.ScanAsync(sqs, Dlq, CancellationToken.None);
        var receives = queue.Receives;
        await scanner.ScanAsync(sqs, Dlq, CancellationToken.None);
        queue.Receives.Should().Be(receives, "many replays on one queue share one scan");

        time.Advance(TimeSpan.FromSeconds(61));
        await scanner.ScanAsync(sqs, Dlq, CancellationToken.None);
        queue.Receives.Should().BeGreaterThan(receives);

        // An incomplete scan is never remembered: the very next one tries again.
        var broken = new FakeQueue(20);
        broken.NeverHandedOut.Add("m-3");
        var brokenSqs = broken.Client();
        var other = Scanner(time);
        (await other.ScanAsync(brokenSqs, Dlq, CancellationToken.None)).Complete.Should().BeFalse();
        var afterFirst = broken.Receives;
        (await other.ScanAsync(brokenSqs, Dlq, CancellationToken.None)).Complete.Should().BeFalse();
        broken.Receives.Should().BeGreaterThan(afterFirst);
    }

    // ── the check built on the scan ──────────────────────────────────────────────────────────────

    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-04T12:00:00Z");

    private static Namespace Ns() =>
        Namespace.Create("sqs.eu-west-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENGbPxRfiCYEXAMPLEKEY", provider: CloudProviderType.Aws, awsRegion: "eu-west-1", ownerId: "owner1").Value;

    private static RecoveryLedgerEntry Entry(TimeSpan ago, string? marker = "entry-7", string? replayedId = null) => new()
    {
        OperationId = Guid.NewGuid(), OwnerId = "owner1", BodyHash = "h", TargetEntity = "orders", BegunAt = Now - ago, EntityNameSnapshot = "orders",
        RecoveryMarker = marker, MarkerApplied = marker is not null, ReplayedProviderMessageId = replayedId, State = RecoveryEntryState.Observing,
    };

    private static DlqObserverAttestation Attestation(Namespace ns) =>
        new() { OwnerId = ns.OwnerId, NamespaceId = ns.Id, Enabled = true, ObserverReference = "whole-queue-scan", DlqEntityName = "*", StalenessBoundMinutes = 30, LastConfirmedAt = Now };

    private static SqsWholeQueueCheck Check(FakeQueue queue, string? redrive = """{"deadLetterTargetArn":"arn:aws:sqs:eu-west-1:1:orders-dlq","maxReceiveCount":"3"}""", int visibility = 30)
    {
        var sqs = Mock.Get(queue.Client());
        sqs.Setup(s => s.GetQueueUrlAsync(It.Is<GetQueueUrlRequest>(r => r.QueueName == "orders"), It.IsAny<CancellationToken>())).ReturnsAsync(new GetQueueUrlResponse { QueueUrl = "https://sqs.test/1/orders" });
        sqs.Setup(s => s.GetQueueUrlAsync(It.Is<GetQueueUrlRequest>(r => r.QueueName == "orders-dlq"), It.IsAny<CancellationToken>())).ReturnsAsync(new GetQueueUrlResponse { QueueUrl = Dlq });
        var source = new Dictionary<string, string> { ["VisibilityTimeout"] = visibility.ToString() };
        if (redrive is not null) source["RedrivePolicy"] = redrive;
        sqs.Setup(s => s.GetQueueAttributesAsync(It.Is<GetQueueAttributesRequest>(r => r.QueueUrl == "https://sqs.test/1/orders"), It.IsAny<CancellationToken>())).ReturnsAsync(new GetQueueAttributesResponse { Attributes = source });
        var clients = Mock.Of<IAwsClientFactory>(f => f.GetSqsClient(It.IsAny<Namespace>()) == sqs.Object);
        return new SqsWholeQueueCheck(clients, Scanner(), NullLogger<SqsWholeQueueCheck>.Instance, new FakeTimeProvider(Now));
    }

    private static Task<DeadLetterReturnVerdict> Ask(SqsWholeQueueCheck check, RecoveryLedgerEntry entry)
    {
        var ns = Ns();
        return check.CheckAsync(ns, Attestation(ns), entry, liveSince: null);
    }

    [Fact]
    public async Task A_replay_that_is_not_in_the_whole_queue_did_not_come_back()
    {
        (await Ask(Check(new FakeQueue(30)), Entry(TimeSpan.FromHours(2)))).Should().Be(DeadLetterReturnVerdict.NotReturned);
    }

    [Fact]
    public async Task A_replay_found_by_its_marker_came_back()
    {
        (await Ask(Check(new FakeQueue(30, marker: "entry-7", markerAt: 22)), Entry(TimeSpan.FromHours(2)))).Should().Be(DeadLetterReturnVerdict.Returned);
    }

    [Fact]
    public async Task A_replay_with_no_marker_is_found_by_the_id_the_cloud_gave_it()
    {
        (await Ask(Check(new FakeQueue(30)), Entry(TimeSpan.FromHours(2), marker: null, replayedId: "m-9"))).Should().Be(DeadLetterReturnVerdict.Returned);
        (await Ask(Check(new FakeQueue(30)), Entry(TimeSpan.FromHours(2), marker: null, replayedId: "not-there"))).Should().Be(DeadLetterReturnVerdict.NotReturned);
    }

    [Fact]
    public async Task With_nothing_to_recognise_it_by_the_answer_is_cannot_tell_never_fine()
    {
        var verdict = await Ask(Check(new FakeQueue(30)), Entry(TimeSpan.FromHours(2), marker: null, replayedId: null));
        verdict.Should().Be(DeadLetterReturnVerdict.CannotTell("NO_WAY_TO_RECOGNISE_IT"));
    }

    [Fact]
    public async Task It_waits_until_the_message_has_had_time_to_fail_again()
    {
        // 30 s visibility × 3 receives + 60 s = 150 s before "not there" can mean "fixed".
        (await Ask(Check(new FakeQueue(5)), Entry(TimeSpan.FromSeconds(100)))).Should().Be(DeadLetterReturnVerdict.TooEarly);
        (await Ask(Check(new FakeQueue(5)), Entry(TimeSpan.FromSeconds(151)))).Should().Be(DeadLetterReturnVerdict.NotReturned);
    }

    [Fact]
    public async Task An_incomplete_scan_is_cannot_tell_with_its_reason()
    {
        var queue = new FakeQueue(30) { Fifo = true };
        (await Ask(Check(queue), Entry(TimeSpan.FromHours(2)))).Should().Be(DeadLetterReturnVerdict.CannotTell("FIFO_QUEUE"));

        (await Ask(Check(new FakeQueue(30), redrive: null), Entry(TimeSpan.FromHours(2)))).Should().Be(DeadLetterReturnVerdict.CannotTell("NO_DEAD_LETTER_QUEUE"));
        (await Ask(Check(new FakeQueue(30), visibility: 43200), Entry(TimeSpan.FromHours(2)))).Should().Be(DeadLetterReturnVerdict.CannotTell("WINDOW_TOO_SHORT"));
    }

    [Fact]
    public void It_needs_nothing_named_and_reads_AWS_redrive_policies_in_both_forms()
    {
        var check = Check(new FakeQueue(0));
        check.NeedsObserverReference.Should().BeFalse();
        check.ValidateObserverReference(null).Should().BeNull();
        SqsWholeQueueCheck.TryReadRedrive("""{"deadLetterTargetArn":"arn:aws:sqs:eu-west-1:1:q-dlq","maxReceiveCount":5}""", out var name, out var max).Should().BeTrue();
        (name, max).Should().Be(("q-dlq", 5));
        SqsWholeQueueCheck.TryReadRedrive("not json", out _, out _).Should().BeFalse();
        SqsWholeQueueCheck.QueueNameOf(Entry(TimeSpan.Zero) ).Should().Be("orders");
    }
}
