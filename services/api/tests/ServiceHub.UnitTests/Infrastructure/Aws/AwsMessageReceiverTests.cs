using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Aws;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Aws;

public sealed class AwsMessageReceiverTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    private static Namespace BuildNamespace()
    {
        var ns = Namespace.Create(
            "test-aws-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=",
            provider: CloudProviderType.Aws,
            awsRegion: "us-east-1").Value;
        return ns;
    }

    // -------------------------------------------------------------------------
    // Constructor
    // -------------------------------------------------------------------------

    [Fact]
    public void Constructor_WithValidDependencies_DoesNotThrow()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var act = () => new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);
        act.Should().NotThrow();
    }

    // -------------------------------------------------------------------------
    // GetMessageCountAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetMessageCountAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.GetMessageCountAsync(
            TestNamespaceId, "test-queue", null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NS.NotFound");
    }

    [Fact]
    public async Task GetMessageCountAsync_WhenSqsThrows_ReturnsFailure()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Queue not found"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.GetMessageCountAsync(
            TestNamespaceId, "test-queue", null, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // GetMessagesAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PeekMessagesAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.PeekMessagesAsync(
            new GetMessagesRequest(TestNamespaceId, "test-queue", null, false, 10),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task PeekMessagesAsync_WhenQueueUrlFails_ReturnsFailure()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Queue does not exist"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.PeekMessagesAsync(
            new GetMessagesRequest(ns.Id, "test-queue", null, false, 10),
            CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    // -------------------------------------------------------------------------
    // GetVisibilityWindowStatusAsync
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetVisibilityWindowStatusAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.GetVisibilityWindowStatusAsync(
            TestNamespaceId, "test-queue", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NS.NotFound");
    }

    [Fact]
    public async Task GetVisibilityWindowStatusAsync_WhenSqsThrows_ReturnsExternalServiceFailure()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Service unavailable"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.GetVisibilityWindowStatusAsync(
            TestNamespaceId, "test-queue", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().StartWith("AWS.SQS");
    }
}
