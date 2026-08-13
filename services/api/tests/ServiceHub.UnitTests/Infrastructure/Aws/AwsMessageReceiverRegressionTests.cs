using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Aws;
using ServiceHub.Shared.Results;
using SHNamespace = ServiceHub.Core.Entities.Namespace;
using SqsSendRequest = Amazon.SQS.Model.SendMessageRequest;

namespace ServiceHub.UnitTests.Infrastructure.Aws;

/// <summary>
/// Regression pack for the SQS semantics ServiceHub depends on. SQS has no true
/// peek — every browse is a receive — so these tests pin the invariants that keep
/// browsing non-destructive: every received message is released (visibility 0),
/// duplicates are folded, replay sends before it deletes, and manual dead-letter
/// metadata survives the round trip into the DTO fields.
/// </summary>
public sealed class AwsMessageReceiverRegressionTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();
    private const string QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456/reg-queue";
    private const string DlqUrl = "https://sqs.us-east-1.amazonaws.com/123456/reg-queue-dlq";
    private const string QueueName = "reg-queue";

    private static SHNamespace BuildNamespace() =>
        SHNamespace.Create(
            "test-aws-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=",
            provider: CloudProviderType.Aws,
            awsRegion: "us-east-1").Value;

    private static Message BuildSqsMessage(
        string messageId,
        string receiptHandle,
        string body = "body",
        Dictionary<string, MessageAttributeValue>? attributes = null)
        => new()
        {
            MessageId = messageId,
            ReceiptHandle = receiptHandle,
            Body = body,
            Attributes = new Dictionary<string, string>
            {
                ["SentTimestamp"] = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(),
                ["ApproximateReceiveCount"] = "1",
            },
            MessageAttributes = attributes ?? new Dictionary<string, MessageAttributeValue>(),
        };

    /// <summary>
    /// Builds a receiver whose SQS client serves the given receive rounds in order
    /// (empty responses after they run out) and records all visibility releases.
    /// </summary>
    private static (AwsMessageReceiver Sut, Mock<IAmazonSQS> Sqs, List<ChangeMessageVisibilityBatchRequest> Releases)
        BuildSut(params ReceiveMessageResponse[] rounds)
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqs = new Mock<IAmazonSQS>();
        sqs.Setup(s => s.GetQueueUrlAsync(
                It.Is<GetQueueUrlRequest>(r => r.QueueName == QueueName), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueUrlResponse { QueueUrl = QueueUrl });
        sqs.Setup(s => s.GetQueueUrlAsync(
                It.Is<GetQueueUrlRequest>(r => r.QueueName == "reg-queue-dlq"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueUrlResponse { QueueUrl = DlqUrl });
        sqs.Setup(s => s.GetQueueAttributesAsync(
                It.Is<GetQueueAttributesRequest>(r => r.AttributeNames.Contains("RedrivePolicy")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["RedrivePolicy"] = @"{""maxReceiveCount"":5,""deadLetterTargetArn"":""arn:aws:sqs:us-east-1:123456:reg-queue-dlq""}",
                },
            });

        var call = 0;
        sqs.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => call < rounds.Length
                ? rounds[call++]
                : new ReceiveMessageResponse { Messages = [] });

        var releases = new List<ChangeMessageVisibilityBatchRequest>();
        sqs.Setup(s => s.ChangeMessageVisibilityBatchAsync(
                It.IsAny<ChangeMessageVisibilityBatchRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ChangeMessageVisibilityBatchRequest, CancellationToken>((req, _) => releases.Add(req))
            .ReturnsAsync(new ChangeMessageVisibilityBatchResponse());

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>())).Returns(sqs.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);
        return (sut, sqs, releases);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Peek — non-destructive browse invariants
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekMessagesAsync_ReleasesEveryReceivedMessageWithZeroVisibility()
    {
        var (sut, _, releases) = BuildSut(new ReceiveMessageResponse
        {
            Messages =
            [
                BuildSqsMessage("m-1", "rh-1"),
                BuildSqsMessage("m-2", "rh-2"),
                BuildSqsMessage("m-3", "rh-3"),
            ],
        });

        var result = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, QueueName, null, false, 50));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(3);

        // Peek must not consume: every receipt handle is made visible again.
        var releasedHandles = releases.SelectMany(r => r.Entries).ToList();
        releasedHandles.Should().HaveCount(3);
        releasedHandles.Select(e => e.ReceiptHandle).Should().BeEquivalentTo("rh-1", "rh-2", "rh-3");
        releasedHandles.Should().OnlyContain(e => e.VisibilityTimeout == 0);
        releases.Should().OnlyContain(r => r.QueueUrl == QueueUrl);
    }

    [Fact]
    public async Task PeekMessagesAsync_MessageCarriesCorrelationIdAttribute_PopulatesCorrelationId()
    {
        // Regresses the Multi-Cloud Trace gap this fix closes: SQS (like Pub/Sub) has no
        // dedicated SDK correlation field — unlike Azure Service Bus, whose native
        // CorrelationId property was already mapped — so without this extraction,
        // DlqMonitorService persisted every AWS DLQ message with CorrelationId=null, making
        // it permanently unreachable via Cross-Cloud Trace's historical-DLQ lookup once it's
        // no longer peekable live.
        var (sut, _, _) = BuildSut(new ReceiveMessageResponse
        {
            Messages =
            [
                BuildSqsMessage("m-1", "rh-1", attributes: new Dictionary<string, MessageAttributeValue>
                {
                    ["correlationId"] = new MessageAttributeValue { DataType = "String", StringValue = "shs-abc123-0001" },
                }),
            ],
        });

        var result = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, QueueName, null, false, 50));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].CorrelationId.Should().Be("shs-abc123-0001");
    }

    [Fact]
    public async Task PeekMessagesAsync_DeduplicatesRedeliveriesAndReleasesLatestHandle()
    {
        var (sut, _, releases) = BuildSut(
            new ReceiveMessageResponse
            {
                Messages = [BuildSqsMessage("m-1", "rh-old"), BuildSqsMessage("m-2", "rh-2")],
            },
            new ReceiveMessageResponse
            {
                // m-1 redelivered with a fresh receipt handle — only the latest is valid
                Messages = [BuildSqsMessage("m-1", "rh-new"), BuildSqsMessage("m-3", "rh-3")],
            });

        var result = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, QueueName, null, false, 50));

        result.IsSuccess.Should().BeTrue();
        result.Value.Select(m => m.MessageId).Should().BeEquivalentTo("m-1", "m-2", "m-3");

        var handles = releases.SelectMany(r => r.Entries).Select(e => e.ReceiptHandle).ToList();
        handles.Should().Contain("rh-new").And.NotContain("rh-old");
    }

    [Fact]
    public async Task PeekMessagesAsync_HonorsMaxMessagesButStillReleasesExtras()
    {
        var (sut, _, releases) = BuildSut(new ReceiveMessageResponse
        {
            Messages = Enumerable.Range(1, 10)
                .Select(i => BuildSqsMessage($"m-{i}", $"rh-{i}"))
                .ToList(),
        });

        var result = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, QueueName, null, false, 4));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(4);
        // The 6 messages beyond the page size were still received → must be released too.
        releases.SelectMany(r => r.Entries).Should().HaveCount(10);
    }

    [Fact]
    public async Task PeekMessagesAsync_SequenceNumberIsStableAcrossPeeks()
    {
        var (sut, _, _) = BuildSut(
            new ReceiveMessageResponse { Messages = [BuildSqsMessage("stable-id", "rh-a")] },
            new ReceiveMessageResponse { Messages = [BuildSqsMessage("stable-id", "rh-b")] });

        var first = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, QueueName, null, false, 1));
        var second = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, QueueName, null, false, 1));

        // Identity derives from MessageId, not the per-receive receipt handle —
        // this is what makes replay/purge work without a prior-peek cache.
        first.Value[0].SequenceNumber.Should().Be(second.Value[0].SequenceNumber);
    }

    [Theory]
    [InlineData("stable-id")]
    [InlineData("0ee6c520-3396-4ba5-9724-5926f2afcc68")]
    [InlineData("8a57b311-f56c-4cea-a814-65e4e7069393")]
    [InlineData("another-message-id-entirely")]
    public async Task PeekMessagesAsync_SequenceNumberSurvivesJsDoublePrecisionRoundTrip(string messageId)
    {
        // Regression for a real bug: sequence numbers are a SHA-256 hash of the
        // MessageId with no native ordering, so the full 63-bit range produces values
        // like 1650169100759989265 — outside JS's Number.MAX_SAFE_INTEGER (2^53-1).
        // Browsers silently round such values on JSON.parse, so replay/purge requests
        // echoed the corrupted number back and the backend's live re-scan never found
        // a match (404 "MessageNotFound") even though the message was peeked seconds
        // earlier. Masking to 53 bits keeps every value exactly representable as a
        // JS double.
        var (sut, _, _) = BuildSut(
            new ReceiveMessageResponse { Messages = [BuildSqsMessage(messageId, "rh")] });

        var result = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, QueueName, null, false, 1));

        var seq = result.Value[0].SequenceNumber;
        seq.Should().BeLessThanOrEqualTo(9_007_199_254_740_991L); // Number.MAX_SAFE_INTEGER
        ((double)seq).Should().Be(seq, "the value must round-trip exactly through a JS double");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DLQ peek — redrive resolution + dead-letter metadata promotion
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_PromotesDeadLetterMetadataOutOfApplicationProperties()
    {
        var attrs = new Dictionary<string, MessageAttributeValue>
        {
            ["DeadLetterReason"] = new() { DataType = "String", StringValue = "TestingDLQ" },
            ["DeadLetterErrorDescription"] = new() { DataType = "String", StringValue = "manual move" },
            ["orderId"] = new() { DataType = "String", StringValue = "ORD-1" },
        };
        var (sut, sqs, _) = BuildSut(new ReceiveMessageResponse
        {
            Messages = [BuildSqsMessage("m-dl", "rh-dl", "dead body", attrs)],
        });

        var result = await sut.PeekDeadLetterMessagesAsync(new GetMessagesRequest(TestNamespaceId, QueueName, null, true, 10));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        var msg = result.Value[0];
        msg.IsFromDeadLetter.Should().BeTrue();
        // ServiceHub's own dead-letter markers surface as first-class fields …
        msg.DeadLetterReason.Should().Be("TestingDLQ");
        msg.DeadLetterErrorDescription.Should().Be("manual move");
        // … and are not duplicated into user-visible application properties.
        msg.ApplicationProperties.Should().ContainKey("orderId");
        msg.ApplicationProperties.Should().NotContainKey("DeadLetterReason");
        msg.ApplicationProperties.Should().NotContainKey("DeadLetterErrorDescription");
        // The peek went to the redrive target queue, not the source.
        sqs.Verify(s => s.ReceiveMessageAsync(
            It.Is<ReceiveMessageRequest>(r => r.QueueUrl == DlqUrl), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Replay — send-before-delete ordering (no message loss window)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayMessageAsync_SendsToSourceBeforeDeletingFromDlq()
    {
        var target = BuildSqsMessage("m-replay", "rh-replay", "replay body");
        var (sut, sqs, releases) = BuildSut(new ReceiveMessageResponse
        {
            Messages = [target, BuildSqsMessage("m-other", "rh-other")],
        });

        var operations = new List<string>();
        sqs.Setup(s => s.SendMessageAsync(It.IsAny<SqsSendRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => operations.Add("send"))
            .ReturnsAsync(new SendMessageResponse());
        sqs.Setup(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback(() => operations.Add("delete"))
            .ReturnsAsync(new DeleteMessageResponse());

        var seq = ComputeSequenceNumber("m-replay");
        var result = await sut.ReplayMessageAsync(TestNamespaceId, QueueName, null, seq, null);

        result.IsSuccess.Should().BeTrue();
        // If the process dies between the two calls the message still exists in
        // the DLQ (at-least-once) — the reverse order would lose it forever.
        operations.Should().Equal("send", "delete");
        sqs.Verify(s => s.SendMessageAsync(
            It.Is<SqsSendRequest>(r => r.QueueUrl == QueueUrl && r.MessageBody == "replay body"),
            It.IsAny<CancellationToken>()), Times.Once);
        sqs.Verify(s => s.DeleteMessageAsync(
            It.Is<DeleteMessageRequest>(r => r.QueueUrl == DlqUrl && r.ReceiptHandle == "rh-replay"),
            It.IsAny<CancellationToken>()), Times.Once);
        // The non-target message inspected during the scan must be released.
        releases.SelectMany(r => r.Entries).Select(e => e.ReceiptHandle).Should().Contain("rh-other");
    }

    [Fact]
    public async Task ReplayMessageAsync_WithRecoveryMarker_UnderAttributeCap_StampsAttributeAndReportsApplied()
    {
        var target = BuildSqsMessage("m-marker", "rh-marker", "marker body",
            attributes: new Dictionary<string, MessageAttributeValue>
            {
                ["existing-attr"] = new MessageAttributeValue { DataType = "String", StringValue = "keep-me" },
            });
        var (sut, sqs, _) = BuildSut(new ReceiveMessageResponse { Messages = [target] });

        SqsSendRequest? sent = null;
        sqs.Setup(s => s.SendMessageAsync(It.IsAny<SqsSendRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SqsSendRequest, CancellationToken>((req, _) => sent = req)
            .ReturnsAsync(new SendMessageResponse());
        sqs.Setup(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        var seq = ComputeSequenceNumber("m-marker");
        var recoveryMarker = Guid.NewGuid().ToString();
        var result = await sut.ReplayMessageAsync(TestNamespaceId, QueueName, null, seq, recoveryMarker);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeTrue(); // marker applied
        sent.Should().NotBeNull();
        sent!.MessageAttributes["x-servicehub-recovery-id"].StringValue.Should().Be(recoveryMarker);
        // The existing attribute must survive — the marker is added, not swapped in.
        sent.MessageAttributes["existing-attr"].StringValue.Should().Be("keep-me");
    }

    [Fact]
    public async Task ReplayMessageAsync_WithRecoveryMarker_AtAttributeCap_SkipsMarker_NeverDisplacesExisting()
    {
        // SQS caps messages at 10 attributes. With 10 already present, the marker must not be
        // applied — and, critically, none of the 10 existing attributes may be dropped to make
        // room for it.
        var existingAttributes = Enumerable.Range(0, 10)
            .ToDictionary(
                i => $"attr-{i}",
                i => new MessageAttributeValue { DataType = "String", StringValue = $"value-{i}" });

        var target = BuildSqsMessage("m-full", "rh-full", "full body", attributes: existingAttributes);
        var (sut, sqs, _) = BuildSut(new ReceiveMessageResponse { Messages = [target] });

        SqsSendRequest? sent = null;
        sqs.Setup(s => s.SendMessageAsync(It.IsAny<SqsSendRequest>(), It.IsAny<CancellationToken>()))
            .Callback<SqsSendRequest, CancellationToken>((req, _) => sent = req)
            .ReturnsAsync(new SendMessageResponse());
        sqs.Setup(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        var seq = ComputeSequenceNumber("m-full");
        var result = await sut.ReplayMessageAsync(TestNamespaceId, QueueName, null, seq, Guid.NewGuid().ToString());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeFalse(); // marker not applied — the cap was already reached
        sent.Should().NotBeNull();
        sent!.MessageAttributes.Should().NotContainKey("x-servicehub-recovery-id");
        sent.MessageAttributes.Should().HaveCount(10);
        foreach (var (key, value) in existingAttributes)
        {
            sent.MessageAttributes[key].StringValue.Should().Be(value.StringValue);
        }
    }

    [Fact]
    public async Task PurgeMessageAsync_DeletesOnlyTheTargetAndReleasesTheRest()
    {
        var (sut, sqs, releases) = BuildSut(new ReceiveMessageResponse
        {
            Messages =
            [
                BuildSqsMessage("m-keep", "rh-keep"),
                BuildSqsMessage("m-purge", "rh-purge"),
            ],
        });
        sqs.Setup(s => s.DeleteMessageAsync(It.IsAny<DeleteMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteMessageResponse());

        var seq = ComputeSequenceNumber("m-purge");
        var result = await sut.PurgeMessageAsync(TestNamespaceId, QueueName, null, seq, fromDeadLetter: false);

        result.IsSuccess.Should().BeTrue();
        sqs.Verify(s => s.DeleteMessageAsync(
            It.Is<DeleteMessageRequest>(r => r.QueueUrl == QueueUrl && r.ReceiptHandle == "rh-purge"),
            It.IsAny<CancellationToken>()), Times.Once);
        releases.SelectMany(r => r.Entries).Select(e => e.ReceiptHandle)
            .Should().Contain("rh-keep").And.NotContain("rh-purge");
    }

    [Fact]
    public async Task GetMessageCountAsync_SumsVisibleAndInFlight()
    {
        var (sut, sqs, _) = BuildSut();
        sqs.Setup(s => s.GetQueueAttributesAsync(
                It.Is<GetQueueAttributesRequest>(r => r.AttributeNames.Contains("ApproximateNumberOfMessages")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "7",
                    ["ApproximateNumberOfMessagesNotVisible"] = "3",
                },
            });

        var result = await sut.GetMessageCountAsync(TestNamespaceId, QueueName);

        result.IsSuccess.Should().BeTrue();
        // In-flight messages are still in the queue — hiding them made counts
        // read 0 during scans, which users report as "my messages vanished".
        result.Value.Should().Be(10);
    }

    // Mirrors AwsMessageReceiver.ComputeSequenceNumber so tests can address
    // messages the same way replay/purge callers do.
    private static long ComputeSequenceNumber(string messageId)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(messageId));
        return BitConverter.ToInt64(hash, 0) & ((1L << 53) - 1);
    }
}
