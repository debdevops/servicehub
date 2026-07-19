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
/// (pull + immediate nack), DLQ peeks target the "-dlq" convention subscription,
/// replay/purge act on the cached ack ID, and mapping preserves message fidelity.
/// </summary>
public sealed class GcpMessageReceiverRegressionTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();
    private const string SubId = "orders-sub";
    private const string SubResource = "projects/my-project/subscriptions/orders-sub";
    private const string DlqSubResource = "projects/my-project/subscriptions/orders-sub-dlq";

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

    // ─────────────────────────────────────────────────────────────────────────
    // PeekDeadLetterMessagesAsync — targets the "-dlq" convention subscription
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_PullsFromDlqSubscriptionAndFlagsMessages()
    {
        var ns = BuildNamespace();
        var pull = new PullResponse { ReceivedMessages = { BuildReceived("ack-dl", "m-dl", "dead body") } };
        // The DLQ peek must resolve "{entity}-dlq", not the source subscription.
        var (sut, subscriber) = BuildSut(ns, $"{SubId}-dlq", pull);

        var result = await sut.PeekDeadLetterMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, true, 10));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].IsFromDeadLetter.Should().BeTrue();
        result.Value[0].EntityName.Should().Be(SubId);
        subscriber.Verify(s => s.PullAsync(
            It.Is<PullRequest>(r => r.Subscription == DlqSubResource),
            It.IsAny<CancellationToken>()), Times.Once);
        subscriber.Verify(s => s.ModifyAckDeadlineAsync(
            It.Is<ModifyAckDeadlineRequest>(r => r.Subscription == DlqSubResource && r.AckDeadlineSeconds == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ReplayMessageAsync — cached ack ID → nack for redelivery
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayMessageAsync_AfterPeek_NacksCachedAckIdAndInvalidatesCache()
    {
        var ns = BuildNamespace();
        var pull = new PullResponse { ReceivedMessages = { BuildReceived("ack-replay", "m1", "body") } };
        var (sut, subscriber) = BuildSut(ns, SubId, pull);

        var peek = await sut.PeekMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, false, 10));
        var seq = peek.Value[0].SequenceNumber;

        var replay = await sut.ReplayMessageAsync(TestNamespaceId, SubId, null, seq);

        replay.IsSuccess.Should().BeTrue();
        subscriber.Verify(s => s.ModifyAckDeadlineAsync(
            It.Is<ModifyAckDeadlineRequest>(r =>
                r.Subscription == SubResource &&
                r.AckDeadlineSeconds == 0 &&
                r.AckIds.Contains("ack-replay")),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);

        // The ack ID is single-use: a second replay of the same sequence must miss.
        var secondReplay = await sut.ReplayMessageAsync(TestNamespaceId, SubId, null, seq);
        secondReplay.IsSuccess.Should().BeFalse();
        secondReplay.Error.Code.Should().Be("GCP.PubSub.MessageNotFound");
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
    public async Task PurgeMessageAsync_FromDeadLetter_AcknowledgesOnDlqSubscription()
    {
        var ns = BuildNamespace();
        var pull = new PullResponse { ReceivedMessages = { BuildReceived("ack-dl-purge", "m1", "body") } };

        // Peek the DLQ (fills the ack cache), then purge from the DLQ.
        var subscriber = new Mock<SubscriberServiceApiClient>();
        subscriber.Setup(s => s.PullAsync(It.IsAny<PullRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(pull);
        subscriber.Setup(s => s.ModifyAckDeadlineAsync(It.IsAny<ModifyAckDeadlineRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        subscriber.Setup(s => s.AcknowledgeAsync(It.IsAny<AcknowledgeRequest>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, $"{SubId}-dlq", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriber.Object);
        var sut = new GcpMessageReceiver(factory.Object, BuildRepo(ns).Object, NullLogger<GcpMessageReceiver>.Instance);

        var peek = await sut.PeekDeadLetterMessagesAsync(new GetMessagesRequest(TestNamespaceId, SubId, null, true, 10));
        var seq = peek.Value[0].SequenceNumber;

        var purge = await sut.PurgeMessageAsync(TestNamespaceId, SubId, null, seq, fromDeadLetter: true);

        purge.IsSuccess.Should().BeTrue();
        subscriber.Verify(s => s.AcknowledgeAsync(
            It.Is<AcknowledgeRequest>(r =>
                r.Subscription == DlqSubResource && r.AckIds.Contains("ack-dl-purge")),
            It.IsAny<CancellationToken>()), Times.Once);
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
