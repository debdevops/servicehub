using FluentAssertions;
using Google.Cloud.PubSub.V1;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Gcp;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Gcp;

/// <summary>
/// Regression pack for <see cref="GcpMessageReceiver"/> happy paths: these pin the
/// provider semantics a developer must be able to rely on — peek is non-destructive
/// (pull + immediate nack), DLQ subscriptions are resolved dynamically from the source
/// subscription's own DeadLetterPolicy (there's no fixed Pub/Sub naming convention),
/// replay/purge act on the cached ack ID, and mapping preserves message fidelity.
/// </summary>
public sealed class GcpMessageReceiverRegressionTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();
    private const string SubId = "orders-sub";
    private const string SubResource = "projects/my-project/subscriptions/orders-sub";

    private static Namespace BuildNamespace() =>
        Namespace.Create(
            "test-gcp-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=",
            provider: CloudProviderType.Gcp,
            gcpProjectId: "my-project").Value;

    private static Mock<INamespaceRepository> BuildRepo(Namespace ns)
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));
        return repo;
    }

    private static ReceivedMessage BuildReceived(string ackId, string messageId, string body, int deliveryAttempt = 1)
        => new()
        {
            AckId = ackId,
            DeliveryAttempt = deliveryAttempt,
            Message = new PubsubMessage
            {
                MessageId = messageId,
                Data = Google.Protobuf.ByteString.CopyFromUtf8(body),
                Attributes = { ["origin"] = "regression" },
            },
        };

    private static (GcpMessageReceiver Sut, Mock<SubscriberServiceApiClient> Subscriber) BuildSut(
        Namespace ns, string subscriptionId, PullResponse pullResponse)
    {
        var subscriber = new Mock<SubscriberServiceApiClient>();
        subscriber.Setup(s => s.PullAsync(It.IsAny<PullRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pullResponse);
        subscriber.Setup(s => s.ModifyAckDeadlineAsync(It.IsAny<ModifyAckDeadlineRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        subscriber.Setup(s => s.AcknowledgeAsync(It.IsAny<AcknowledgeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, subscriptionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriber.Object);

        var sut = new GcpMessageReceiver(factory.Object, BuildRepo(ns).Object, NullLogger<GcpMessageReceiver>.Instance);
        return (sut, subscriber);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PeekMessagesAsync — non-destructive read invariant
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekMessagesAsync_MapsMessagesAndImmediatelyNacksEverything()
    {
        var ns = BuildNamespace();
        var pull = new PullResponse
        {
            ReceivedMessages =
            {
                BuildReceived("ack-1", "m1", "{\"orderId\":1}", deliveryAttempt: 2),
                BuildReceived("ack-2", "m2", "{\"orderId\":2}"),
            },
        };
        var (sut, subscriber) = BuildSut(ns, SubId, pull);

        var result = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, false, 10));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].MessageId.Should().Be("m1");
        result.Value[0].Body.Should().Be("{\"orderId\":1}");
        result.Value[0].DeliveryCount.Should().Be(2);
        result.Value[0].IsFromDeadLetter.Should().BeFalse();
        result.Value[0].EntityName.Should().Be(SubId);
        result.Value[0].ApplicationProperties.Should().ContainKey("origin");

        // The core Pub/Sub peek invariant: every pulled message is nacked
        // (ack deadline 0) so no consumer is starved and nothing is consumed.
        subscriber.Verify(s => s.ModifyAckDeadlineAsync(
            It.Is<ModifyAckDeadlineRequest>(r =>
                r.Subscription == SubResource &&
                r.AckDeadlineSeconds == 0 &&
                r.AckIds.Count == 2 &&
                r.AckIds.Contains("ack-1") &&
                r.AckIds.Contains("ack-2")),
            It.IsAny<CancellationToken>()), Times.Once);
        subscriber.Verify(s => s.AcknowledgeAsync(It.IsAny<AcknowledgeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PeekMessagesAsync_MessageCarriesCorrelationIdAttribute_PopulatesCorrelationId()
    {
        // Regresses the Multi-Cloud Trace gap this fix closes: Pub/Sub (like SQS) has no
        // dedicated SDK correlation field — unlike Azure Service Bus, whose native
        // CorrelationId property was already mapped — so without this extraction,
        // DlqMonitorService persisted every GCP DLQ message with CorrelationId=null, making
        // it permanently unreachable via Cross-Cloud Trace's historical-DLQ lookup once it's
        // no longer peekable live. Confirmed live: 0/24 GCP DLQ rows had CorrelationId set,
        // vs 13/13 for Azure.
        var ns = BuildNamespace();
        var pull = new PullResponse
        {
            ReceivedMessages =
            {
                new ReceivedMessage
                {
                    AckId = "ack-1",
                    DeliveryAttempt = 1,
                    Message = new PubsubMessage
                    {
                        MessageId = "m1",
                        Data = Google.Protobuf.ByteString.CopyFromUtf8("{}"),
                        Attributes = { ["correlationId"] = "shs-abc123-0001" },
                    },
                },
            },
        };
        var (sut, _) = BuildSut(ns, SubId, pull);

        var result = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, false, 10));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].CorrelationId.Should().Be("shs-abc123-0001");
    }

    [Fact]
    public async Task PeekMessagesAsync_EmptySubscription_ReturnsEmptyWithoutNack()
    {
        var ns = BuildNamespace();
        var (sut, subscriber) = BuildSut(ns, SubId, new PullResponse());

        var result = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, false, 10));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        subscriber.Verify(s => s.ModifyAckDeadlineAsync(It.IsAny<ModifyAckDeadlineRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task PeekMessagesAsync_SameAckId_YieldsStableSequenceNumberAcrossPeeks()
    {
        var ns = BuildNamespace();
        var pull = new PullResponse { ReceivedMessages = { BuildReceived("ack-stable", "m1", "body") } };
        var (sut, _) = BuildSut(ns, SubId, pull);

        var first = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, false, 10));
        var second = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, false, 10));

        // Stable identity is what makes replay/purge targetable after any peek.
        first.Value[0].SequenceNumber.Should().Be(second.Value[0].SequenceNumber);
        first.Value[0].SequenceNumber.Should().BePositive();
    }

    [Theory]
    [InlineData("m1")]
    [InlineData("0ee6c520-3396-4ba5-9724-5926f2afcc68")]
    [InlineData("8a57b311-f56c-4cea-a814-65e4e7069393")]
    [InlineData("another-message-id-entirely")]
    public async Task PeekMessagesAsync_SequenceNumberSurvivesJsDoublePrecisionRoundTrip(string messageId)
    {
        // Regression for the same class of bug fixed on the AWS receiver: sequence
        // numbers are a SHA-256 hash of the MessageId, so the full 63-bit range
        // produces values outside JS's Number.MAX_SAFE_INTEGER (2^53-1). Browsers
        // silently round such values on JSON.parse, so a replay/purge request built
        // from a peeked message would send back a different integer than the one the
        // backend computes on its live re-scan. Masking to 53 bits keeps every value
        // exactly representable as a JS double.
        var ns = BuildNamespace();
        var pull = new PullResponse { ReceivedMessages = { BuildReceived("ack-1", messageId, "body") } };
        var (sut, _) = BuildSut(ns, SubId, pull);

        var result = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, false, 10));

        var seq = result.Value[0].SequenceNumber;
        seq.Should().BeLessThanOrEqualTo(9_007_199_254_740_991L); // Number.MAX_SAFE_INTEGER
        ((double)seq).Should().Be(seq, "the value must round-trip exactly through a JS double");
    }

    [Fact]
    public async Task PeekMessagesAsync_ClientCancellation_PropagatesAsOperationCanceled()
    {
        // Regression: gRPC surfaces a client-cancelled Pull as RpcException(Cancelled), not a
        // plain OperationCanceledException. Left uncaught, this used to fall into the generic
        // catch-all and come back as a 502 "PeekFailed" for a client that had already
        // disconnected. It must now propagate as OperationCanceledException so
        // ErrorHandlingMiddleware's existing client-disconnect handling (499, no error log)
        // takes over instead.
        var ns = BuildNamespace();
        var subscriber = new Mock<SubscriberServiceApiClient>();
        subscriber.Setup(s => s.PullAsync(It.IsAny<PullRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Cancelled, "Call canceled by the client.")));

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, SubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriber.Object);

        var sut = new GcpMessageReceiver(factory.Object, BuildRepo(ns).Object, NullLogger<GcpMessageReceiver>.Instance);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Func<Task> act = () => sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, false, 10), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PeekDeadLetterMessagesAsync — resolves the DLQ subscription dynamically from
    // the source subscription's own DeadLetterPolicy (no fixed naming convention).
    // The success path (GetSubscriptionAsync → ListTopicSubscriptionsAsync → pull) isn't
    // asserted end-to-end here: GAX's PagedAsyncEnumerable has no public constructor to
    // fake in a unit test (same limitation noted in
    // GcpMessagingProviderTests.ListEntitiesAsync_WhenExceptionOccurs_ReturnsExternalServiceFailure),
    // so this pins the deterministic no-policy-configured path instead.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_NoDeadLetterPolicyConfigured_ReturnsEmpty()
    {
        var ns = BuildNamespace();
        var subscriber = new Mock<SubscriberServiceApiClient>();
        subscriber.Setup(s => s.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subscription());
        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, SubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriber.Object);
        var sut = new GcpMessageReceiver(factory.Object, BuildRepo(ns).Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.PeekDeadLetterMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, true, 10));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        subscriber.Verify(s => s.GetSubscriptionAsync(
            It.Is<GetSubscriptionRequest>(r => r.Subscription == SubResource),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ReplayMessageAsync — cached ack ID → nack for redelivery
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayMessageAsync_NoDeadLetterPolicyConfigured_ReturnsValidationFailure()
    {
        // Replay always targets the DLQ (mirrors AWS, which always resolves and scans its DLQ
        // queue here too), resolved the same way Peek/Purge-from-DLQ do — via the source
        // subscription's own DeadLetterPolicy. With none configured, replay fails before ever
        // scanning for the target message.
        var ns = BuildNamespace();
        var subscriber = new Mock<SubscriberServiceApiClient>();
        subscriber.Setup(s => s.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subscription());
        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, SubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriber.Object);
        var sut = new GcpMessageReceiver(factory.Object, BuildRepo(ns).Object, NullLogger<GcpMessageReceiver>.Instance);

        var replay = await sut.ReplayMessageAsync(TestNamespaceId, SubId, null, sequenceNumber: 123456, recoveryMarker: null);

        replay.IsSuccess.Should().BeFalse();
        replay.Error.Code.Should().Be("GCP.PubSub.NoDlq");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PurgeMessageAsync — acknowledge = delete
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeMessageAsync_AfterPeek_AcknowledgesOnSourceSubscription()
    {
        var ns = BuildNamespace();
        var pull = new PullResponse { ReceivedMessages = { BuildReceived("ack-purge", "m1", "body") } };
        var (sut, subscriber) = BuildSut(ns, SubId, pull);

        var peek = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, false, 10));
        var seq = peek.Value[0].SequenceNumber;

        var purge = await sut.PurgeMessageAsync(TestNamespaceId, SubId, null, seq, fromDeadLetter: false);

        purge.IsSuccess.Should().BeTrue();
        subscriber.Verify(s => s.AcknowledgeAsync(
            It.Is<AcknowledgeRequest>(r =>
                r.Subscription == SubResource && r.AckIds.Contains("ack-purge")),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PurgeMessageAsync_FromDeadLetter_NoDeadLetterPolicyConfigured_ReturnsValidationFailure()
    {
        // Purging from the DLQ resolves the DLQ subscription the same way peek does (via the
        // source subscription's DeadLetterPolicy); with none configured there's nothing to purge
        // from, and purge fails before ever scanning for the target message.
        var ns = BuildNamespace();
        var subscriber = new Mock<SubscriberServiceApiClient>();
        subscriber.Setup(s => s.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subscription());
        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, SubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriber.Object);
        var sut = new GcpMessageReceiver(factory.Object, BuildRepo(ns).Object, NullLogger<GcpMessageReceiver>.Instance);

        var purge = await sut.PurgeMessageAsync(TestNamespaceId, SubId, null, sequenceNumber: 123456, fromDeadLetter: true);

        purge.IsSuccess.Should().BeFalse();
        purge.Error.Code.Should().Be("GCP.PubSub.NoDlq");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetAckDeadlineStatusAsync — success path
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAckDeadlineStatusAsync_ReturnsPolicySnapshot()
    {
        var ns = BuildNamespace();
        var subscriber = new Mock<SubscriberServiceApiClient>();
        subscriber.Setup(s => s.GetSubscriptionAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subscription
            {
                AckDeadlineSeconds = 30,
                EnableMessageOrdering = true,
                DeadLetterPolicy = new DeadLetterPolicy
                {
                    DeadLetterTopic = "projects/my-project/topics/orders-dl",
                    MaxDeliveryAttempts = 7,
                },
            });
        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, SubId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriber.Object);
        var sut = new GcpMessageReceiver(factory.Object, BuildRepo(ns).Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.GetAckDeadlineStatusAsync(TestNamespaceId, SubId);

        result.IsSuccess.Should().BeTrue();
        result.Value.AckDeadlineSeconds.Should().Be(30);
        result.Value.HasDeadLetterPolicy.Should().BeTrue();
        result.Value.DeadLetterTopic.Should().Be("projects/my-project/topics/orders-dl");
        result.Value.MaxDeliveryAttempts.Should().Be(7);
        result.Value.MessageOrderingEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task GetScheduledMessagesAsync_UnsupportedOnPubSub_ReturnsEmptySuccess()
    {
        var sut = new GcpMessageReceiver(
            new Mock<IGcpClientFactory>().Object,
            new Mock<INamespaceRepository>().Object,
            NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.GetScheduledMessagesAsync(TestNamespaceId, SubId, null, 10);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
