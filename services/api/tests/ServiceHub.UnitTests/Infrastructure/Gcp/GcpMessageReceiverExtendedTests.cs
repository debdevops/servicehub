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
/// Extended tests for <see cref="GcpMessageReceiver"/> covering additional
/// error paths, DLQ, DeadLetterMessages, and ReplayMessage scenarios.
/// </summary>
public sealed class GcpMessageReceiverExtendedTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    private static Namespace BuildNamespace() =>
        Namespace.Create(
            "test-gcp-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=",
            provider: CloudProviderType.Gcp,
            gcpProjectId: "my-project").Value;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor guards
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullFactory_Throws()
    {
        var act = () => new GcpMessageReceiver(null!,
            new Mock<INamespaceRepository>().Object,
            NullLogger<GcpMessageReceiver>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clientFactory");
    }

    [Fact]
    public void Constructor_NullRepo_Throws()
    {
        var act = () => new GcpMessageReceiver(
            new Mock<IGcpClientFactory>().Object,
            null!,
            NullLogger<GcpMessageReceiver>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new GcpMessageReceiver(
            new Mock<IGcpClientFactory>().Object,
            new Mock<INamespaceRepository>().Object,
            null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetMessageCountAsync — normalizes to neutral success (Pub/Sub has no count API)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("my-subscription")]
    [InlineData("another-sub")]
    public async Task GetMessageCountAsync_ReturnsNeutralSuccess_Regardless(string subName)
    {
        var sut = new GcpMessageReceiver(
            new Mock<IGcpClientFactory>().Object,
            new Mock<INamespaceRepository>().Object,
            NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.GetMessageCountAsync(TestNamespaceId, subName);

        // Pub/Sub has no direct count API — normalized to a neutral success (0) so it
        // behaves the same shape as the other providers rather than surfacing an error.
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PeekMessagesAsync — null throws
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekMessagesAsync_NullRequest_Throws()
    {
        var sut = new GcpMessageReceiver(
            new Mock<IGcpClientFactory>().Object,
            new Mock<INamespaceRepository>().Object,
            NullLogger<GcpMessageReceiver>.Instance);

        var act = async () => await sut.PeekMessagesAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PeekMessagesAsync — subscriber throws → ExternalService error
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekMessagesAsync_WhenSubscriberThrows_ReturnsExternalServiceFailure()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "gRPC unavailable")));

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.PeekMessagesAsync(
            new GetMessagesRequest(TestNamespaceId, "my-sub", null, false, 10));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("GCP.PubSub.PeekFailed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PeekDeadLetterMessagesAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_NullRequest_Throws()
    {
        var sut = new GcpMessageReceiver(
            new Mock<IGcpClientFactory>().Object,
            new Mock<INamespaceRepository>().Object,
            NullLogger<GcpMessageReceiver>.Instance);

        var act = async () => await sut.PeekDeadLetterMessagesAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new GcpMessageReceiver(
            new Mock<IGcpClientFactory>().Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.PeekDeadLetterMessagesAsync(
            new GetMessagesRequest(TestNamespaceId, "my-sub", null, true, 10));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NS.NotFound");
    }

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_WhenSubscriberThrows_ReturnsDlqPeekFailed()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.NotFound, "Subscription not found")));

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.PeekDeadLetterMessagesAsync(
            new GetMessagesRequest(TestNamespaceId, "my-sub", null, true, 10));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("GCP.PubSub.DlqPeekFailed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // DeadLetterMessagesAsync — pull → republish to dead-letter topic → ack
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeadLetterMessagesAsync_NullRequest_Throws()
    {
        var sut = new GcpMessageReceiver(
            new Mock<IGcpClientFactory>().Object,
            new Mock<INamespaceRepository>().Object,
            NullLogger<GcpMessageReceiver>.Instance);

        var act = async () => await sut.DeadLetterMessagesAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task DeadLetterMessagesAsync_NoDeadLetterPolicy_ReturnsNoDlqValidation()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var subscriber = new Mock<SubscriberServiceApiClient>();
        subscriber.Setup(s => s.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subscription()); // no DeadLetterPolicy

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, "my-sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriber.Object);

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.DeadLetterMessagesAsync(new DeadLetterRequest(TestNamespaceId, "my-sub", SubscriptionName: null, MessageCount: 1, Reason: "manual reason"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("GCP.PubSub.NoDlq");
    }

    [Fact]
    public async Task DeadLetterMessagesAsync_WithPolicy_RepublishesToDeadLetterTopicAndAcks()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var subscriber = new Mock<SubscriberServiceApiClient>();
        subscriber.Setup(s => s.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subscription
            {
                DeadLetterPolicy = new DeadLetterPolicy { DeadLetterTopic = "projects/my-project/topics/orders-dl" }
            });
        subscriber.Setup(s => s.PullAsync(It.IsAny<PullRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PullResponse
            {
                ReceivedMessages =
                {
                    new ReceivedMessage
                    {
                        AckId = "ack-1",
                        Message = new PubsubMessage
                        {
                            MessageId = "m1",
                            Data = Google.Protobuf.ByteString.CopyFromUtf8("{\"orderId\":1}"),
                            Attributes = { ["source"] = "test" }
                        }
                    },
                    new ReceivedMessage
                    {
                        AckId = "ack-2",
                        Message = new PubsubMessage
                        {
                            MessageId = "m2",
                            Data = Google.Protobuf.ByteString.CopyFromUtf8("{\"orderId\":2}")
                        }
                    }
                }
            });

        AcknowledgeRequest? ackRequest = null;
        subscriber.Setup(s => s.AcknowledgeAsync(It.IsAny<AcknowledgeRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AcknowledgeRequest, CancellationToken>((req, _) => ackRequest = req)
            .Returns(Task.CompletedTask);

        var published = new List<PubsubMessage>();
        var publisher = new Mock<PublisherClient>();
        publisher.Setup(p => p.PublishAsync(It.IsAny<PubsubMessage>()))
            .Callback<PubsubMessage>(published.Add)
            .ReturnsAsync("published-id");

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, "my-sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriber.Object);
        factory.Setup(f => f.GetPublisherClientAsync(ns, "orders-dl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(publisher.Object);

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.DeadLetterMessagesAsync(new DeadLetterRequest(
            TestNamespaceId, "my-sub", SubscriptionName: null,
            MessageCount: 2, Reason: "TestingDLQ", ErrorDescription: "moved via ServiceHub"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(2);

        // Republished to the policy's dead-letter topic with reason attributes, originals preserved
        published.Should().HaveCount(2);
        published[0].Attributes["DeadLetterReason"].Should().Be("TestingDLQ");
        published[0].Attributes["DeadLetterErrorDescription"].Should().Be("moved via ServiceHub");
        published[0].Attributes["source"].Should().Be("test");

        // Originals acknowledged (removed from the source subscription)
        ackRequest.Should().NotBeNull();
        ackRequest!.AckIds.Should().BeEquivalentTo(new[] { "ack-1", "ack-2" });
    }

    [Fact]
    public async Task DeadLetterMessagesAsync_EmptySourceSubscription_ReturnsZero()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var subscriber = new Mock<SubscriberServiceApiClient>();
        subscriber.Setup(s => s.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subscription
            {
                DeadLetterPolicy = new DeadLetterPolicy { DeadLetterTopic = "projects/my-project/topics/orders-dl" }
            });
        subscriber.Setup(s => s.PullAsync(It.IsAny<PullRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PullResponse());

        var publisher = new Mock<PublisherClient>();
        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, "my-sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriber.Object);
        factory.Setup(f => f.GetPublisherClientAsync(ns, "orders-dl", It.IsAny<CancellationToken>()))
            .ReturnsAsync(publisher.Object);

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.DeadLetterMessagesAsync(new DeadLetterRequest(TestNamespaceId, "my-sub", SubscriptionName: null, MessageCount: 3, Reason: "TestingDLQ"));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        publisher.Verify(p => p.PublishAsync(It.IsAny<PubsubMessage>()), Times.Never);
        subscriber.Verify(s => s.AcknowledgeAsync(It.IsAny<AcknowledgeRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeadLetterMessagesAsync_ClientError_ReturnsDlqFailed()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, "my-sub", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("credential failure"));

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.DeadLetterMessagesAsync(new DeadLetterRequest(TestNamespaceId, "my-sub", SubscriptionName: null, MessageCount: 1, Reason: "TestingDLQ"));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("GCP.PubSub.DlqFailed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ReplayMessageAsync — sequence not in cache
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayMessageAsync_NoDeadLetterPolicyConfigured_ReturnsNoDlqValidation()
    {
        // Replay always resolves the DLQ subscription from the source subscription's own
        // DeadLetterPolicy (there's no cross-request cache to check anymore — see
        // GcpMessageReceiver's class remarks on why replay/purge re-scan instead of caching).
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var subscriber = new Mock<SubscriberServiceApiClient>();
        subscriber.Setup(s => s.GetSubscriptionAsync(It.IsAny<GetSubscriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Subscription()); // no DeadLetterPolicy

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(ns, "my-sub", It.IsAny<CancellationToken>()))
            .ReturnsAsync(subscriber.Object);

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.ReplayMessageAsync(TestNamespaceId, "my-sub", null, 42L);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("GCP.PubSub.NoDlq");
    }
}
