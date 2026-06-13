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
/// Extended tests for <see cref="GcpMessageSender"/> covering additional paths:
/// null EntityName validation, application properties mapping, ordering key mapping,
/// and batch publishing of multiple messages.
/// </summary>
public sealed class GcpMessageSenderExtendedTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    private static Namespace BuildNamespace() =>
        Namespace.Create(
            "test-gcp-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=",
            provider: CloudProviderType.Gcp,
            gcpProjectId: "my-project").Value;

    // ─────────────────────────────────────────────────────────────────────────
    // SendAsync — null EntityName
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_NullEntityName_ReturnsValidationFailure()
    {
        var repo = new Mock<INamespaceRepository>();
        var sut = new GcpMessageSender(new Mock<IGcpClientFactory>().Object, repo.Object,
            NullLogger<GcpMessageSender>.Instance);

        // EntityName = null triggers the guard
        var req = new SendMessageRequest(TestNamespaceId, null!, "body");
        var result = await sut.SendAsync(req);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GCP.PubSub.InvalidRequest");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendAsync — publisher succeeds
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_WhenPublisherSucceeds_ReturnsSuccess()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var publisherClient = new Mock<PublisherClient>();
        publisherClient.Setup(p => p.PublishAsync(It.IsAny<PubsubMessage>()))
            .ReturnsAsync("published-msg-id");

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetPublisherClientAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(publisherClient.Object);

        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        var result = await sut.SendAsync(new SendMessageRequest(TestNamespaceId, "my-topic", "hello gcp"));

        result.IsSuccess.Should().BeTrue();
        publisherClient.Verify(p => p.PublishAsync(It.IsAny<PubsubMessage>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_WithSessionId_MapsToOrderingKey()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        PubsubMessage? capturedMessage = null;
        var publisherClient = new Mock<PublisherClient>();
        publisherClient.Setup(p => p.PublishAsync(It.IsAny<PubsubMessage>()))
            .Callback<PubsubMessage>(m => capturedMessage = m)
            .ReturnsAsync("msg-ordered");

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetPublisherClientAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(publisherClient.Object);

        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        await sut.SendAsync(new SendMessageRequest(TestNamespaceId, "my-topic", "ordered msg",
            SessionId: "order-key-xyz"));

        capturedMessage.Should().NotBeNull();
        capturedMessage!.OrderingKey.Should().Be("order-key-xyz");
    }

    [Fact]
    public async Task SendAsync_WithApplicationProperties_MapsToAttributes()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        PubsubMessage? capturedMessage = null;
        var publisherClient = new Mock<PublisherClient>();
        publisherClient.Setup(p => p.PublishAsync(It.IsAny<PubsubMessage>()))
            .Callback<PubsubMessage>(m => capturedMessage = m)
            .ReturnsAsync("msg-with-props");

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetPublisherClientAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(publisherClient.Object);

        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        await sut.SendAsync(new SendMessageRequest(
            TestNamespaceId, "my-topic", "msg",
            ApplicationProperties: new Dictionary<string, object> { ["env"] = "prod", ["version"] = "2" }));

        capturedMessage.Should().NotBeNull();
        capturedMessage!.Attributes.Should().ContainKey("env").WhoseValue.Should().Be("prod");
        capturedMessage.Attributes.Should().ContainKey("version").WhoseValue.Should().Be("2");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendBatchAsync — success path with multiple messages
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendBatchAsync_WhenPublisherSucceeds_ReturnsSuccess()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        int publishCount = 0;
        var publisherClient = new Mock<PublisherClient>();
        publisherClient.Setup(p => p.PublishAsync(It.IsAny<PubsubMessage>()))
            .Callback(() => publishCount++)
            .ReturnsAsync("batch-msg-id");

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetPublisherClientAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(publisherClient.Object);

        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        var requests = new[]
        {
            new SendMessageRequest(TestNamespaceId, "my-topic", "msg-1"),
            new SendMessageRequest(TestNamespaceId, "my-topic", "msg-2"),
            new SendMessageRequest(TestNamespaceId, "my-topic", "msg-3"),
        };

        var result = await sut.SendBatchAsync(requests);

        result.IsSuccess.Should().BeTrue();
        publishCount.Should().Be(3);
    }

    [Fact]
    public async Task SendBatchAsync_FirstItemNullEntityName_ReturnsValidationFailure()
    {
        var sut = new GcpMessageSender(new Mock<IGcpClientFactory>().Object,
            new Mock<INamespaceRepository>().Object, NullLogger<GcpMessageSender>.Instance);

        var requests = new[]
        {
            new SendMessageRequest(TestNamespaceId, null!, "body")
        };

        var result = await sut.SendBatchAsync(requests);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GCP.PubSub.InvalidRequest");
    }
}
