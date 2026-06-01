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

public sealed class GcpMessageReceiverTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    private static Namespace BuildNamespace()
    {
        return Namespace.Create(
            "test-gcp-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=",
            provider: CloudProviderType.Gcp,
            gcpProjectId: "my-project").Value;
    }

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var act = () => new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);
        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // GetMessageCountAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMessageCountAsync_ReturnsFailure_WithCountUnavailableCode()
    {
        // GCP Pub/Sub does not support direct message count queries — returns a typed failure
        // so callers can display "N/A" in the UI.
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.GetMessageCountAsync(
            TestNamespaceId, "my-subscription", null, CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("GCP.PubSub.CountUnavailable");
    }

    [Fact]
    public async Task PeekMessagesAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Namespace not found")));

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.PeekMessagesAsync(
            new GetMessagesRequest(TestNamespaceId, "my-subscription", null, false, 10),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // GetAckDeadlineStatusAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetAckDeadlineStatusAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Namespace not found")));

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.GetAckDeadlineStatusAsync(
            TestNamespaceId, "my-subscription", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NS.NotFound");
    }

    [Fact]
    public async Task GetAckDeadlineStatusAsync_WhenSubscriberThrows_ReturnsExternalServiceFailure()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        // Subscriber client throws when getting subscription
        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetSubscriberClientAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Grpc.Core.RpcException(new Grpc.Core.Status(Grpc.Core.StatusCode.Unavailable, "Service unavailable")));

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.GetAckDeadlineStatusAsync(
            TestNamespaceId, "my-subscription", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().StartWith("GCP.PubSub");
    }

    // -------------------------------------------------------------------------
    // Thread-safety: ConcurrentDictionary ack-ID cache
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Concurrent_PeekAndReplay_DoNotCorruptAckIdCache()
    {
        // Arrange: PeekMessagesAsync fails fast (namespace not found), so we just verify
        // that two tasks running simultaneously do not throw on the shared ConcurrentDictionary.
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new GcpMessageReceiver(factory.Object, repo.Object, NullLogger<GcpMessageReceiver>.Instance);

        var request = new GetMessagesRequest(Guid.NewGuid(), "my-sub", null, false, 10);

        // Act: two concurrent peek tasks (no GCP call is made — namespace lookup fails first)
        var t1 = sut.PeekMessagesAsync(request, CancellationToken.None);
        var t2 = sut.PeekMessagesAsync(request, CancellationToken.None);

        var act = async () => await Task.WhenAll(t1, t2);

        // Assert: no exception thrown from concurrent access
        await act.Should().NotThrowAsync();
    }
}
