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
    // GetMessageCountAsync — always returns -1 (Pub/Sub limitation)
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("my-subscription")]
    [InlineData("another-sub")]
    public async Task GetMessageCountAsync_ReturnsNegativeOne_Regardless(string subName)
    {
        var sut = new GcpMessageReceiver(
            new Mock<IGcpClientFactory>().Object,
            new Mock<INamespaceRepository>().Object,
            NullLogger<GcpMessageReceiver>.Instance);

        var result = await sut.GetMessageCountAsync(TestNamespaceId, subName);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(-1L);
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
    // DeadLetterMessagesAsync — always returns validation failure for GCP
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
    public async Task DeadLetterMessagesAsync_AlwaysReturnsValidationFailure()
    {
        // GCP Pub/Sub manual DLQ is not supported — requires policy-driven approach
        var sut = new GcpMessageReceiver(
            new Mock<IGcpClientFactory>().Object,
            new Mock<INamespaceRepository>().Object,
            NullLogger<GcpMessageReceiver>.Instance);

        var request = new DeadLetterRequest(TestNamespaceId, "my-sub", "manual reason", 1);

        var result = await sut.DeadLetterMessagesAsync(request);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("GCP.PubSub.NoManualDlq");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ReplayMessageAsync — sequence not in cache
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayMessageAsync_WhenSequenceNotInCache_ReturnsNotFound()
    {
        var sut = new GcpMessageReceiver(
            new Mock<IGcpClientFactory>().Object,
            new Mock<INamespaceRepository>().Object,
            NullLogger<GcpMessageReceiver>.Instance);

        // Sequence 42 was never cached (no prior peek)
        var result = await sut.ReplayMessageAsync(TestNamespaceId, "my-sub", null, 42L);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("GCP.PubSub.MessageNotFound");
    }
}
