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
/// Tests for <see cref="GcpMessageSender"/> and <see cref="GcpMessagingProvider"/>.
/// </summary>
public sealed class GcpMessageSenderTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    private static Namespace BuildNamespace() =>
        Namespace.Create(
            "test-gcp-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=",
            provider: CloudProviderType.Gcp,
            gcpProjectId: "my-project").Value;

    // ─────────────────────────────────────────────────────────────────────────
    // GcpMessageSender Constructor
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullFactory_Throws()
    {
        var repo = new Mock<INamespaceRepository>();
        var act = () => new GcpMessageSender(null!, repo.Object, NullLogger<GcpMessageSender>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clientFactory");
    }

    [Fact]
    public void Constructor_NullRepo_Throws()
    {
        var factory = new Mock<IGcpClientFactory>();
        var act = () => new GcpMessageSender(factory.Object, null!, NullLogger<GcpMessageSender>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var act = () => new GcpMessageSender(factory.Object, repo.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ValidArgs_DoesNotThrow()
    {
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var act = () => new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendAsync — validation
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_NullRequest_Throws()
    {
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        var act = async () => await sut.SendAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_NullNamespaceId_ReturnsValidationFailure()
    {
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        var request = new SendMessageRequest(null, "my-topic", "body");

        var result = await sut.SendAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("GCP.PubSub.InvalidRequest");
    }

    [Fact]
    public async Task SendAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        var request = new SendMessageRequest(TestNamespaceId, "my-topic", "body");
        var result = await sut.SendAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NS.NotFound");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendAsync — publisher client throws
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_WhenPublisherClientThrows_ReturnsExternalServiceFailure()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetPublisherClientAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Auth error"));

        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        var request = new SendMessageRequest(TestNamespaceId, "my-topic", "body");
        var result = await sut.SendAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("GCP.PubSub.SendFailed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendBatchAsync — empty returns success
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendBatchAsync_EmptyCollection_ReturnsSuccess()
    {
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        var result = await sut.SendBatchAsync([], CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendBatchAsync_NullCollection_Throws()
    {
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        var act = async () => await sut.SendBatchAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendBatchAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var factory = new Mock<IGcpClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        var requests = new[]
        {
            new SendMessageRequest(TestNamespaceId, "my-topic", "body-1"),
            new SendMessageRequest(TestNamespaceId, "my-topic", "body-2")
        };

        var result = await sut.SendBatchAsync(requests, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NS.NotFound");
    }

    [Fact]
    public async Task SendBatchAsync_WhenPublisherClientThrows_ReturnsExternalServiceFailure()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var factory = new Mock<IGcpClientFactory>();
        factory.Setup(f => f.GetPublisherClientAsync(It.IsAny<Namespace>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Pub/Sub unavailable"));

        var sut = new GcpMessageSender(factory.Object, repo.Object, NullLogger<GcpMessageSender>.Instance);

        var requests = new[]
        {
            new SendMessageRequest(TestNamespaceId, "my-topic", "body-1"),
        };

        var result = await sut.SendBatchAsync(requests, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("GCP.PubSub.BatchSendFailed");
    }
}
