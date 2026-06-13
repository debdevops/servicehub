using Amazon.SimpleNotificationService;
using Amazon.SimpleNotificationService.Model;
using Amazon.SQS;
using Amazon.SQS.Model;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Aws;
using ServiceHub.Shared.Results;
using SHSendMessageRequest = ServiceHub.Core.DTOs.Requests.SendMessageRequest;

namespace ServiceHub.UnitTests.Infrastructure.Aws;

/// <summary>
/// Tests for <see cref="AwsMessageSender"/>.
/// </summary>
public sealed class AwsMessageSenderTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    private static Namespace BuildNamespace() =>
        Namespace.Create(
            "test-aws-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=",
            provider: CloudProviderType.Aws,
            awsRegion: "us-east-1").Value;

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullFactory_Throws()
    {
        var repo = new Mock<INamespaceRepository>();
        var act = () => new AwsMessageSender(null!, repo.Object, NullLogger<AwsMessageSender>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clientFactory");
    }

    [Fact]
    public void Constructor_NullRepo_Throws()
    {
        var factory = new Mock<IAwsClientFactory>();
        var act = () => new AwsMessageSender(factory.Object, null!, NullLogger<AwsMessageSender>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var act = () => new AwsMessageSender(factory.Object, repo.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ValidArgs_DoesNotThrow()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var act = () => new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendAsync — validation errors
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_NullRequest_Throws()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        var act = async () => await sut.SendAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendAsync_NullNamespaceId_ReturnsValidationFailure()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        // NamespaceId = null
        var request = new SHSendMessageRequest(null, "test-queue", "body");

        var result = await sut.SendAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AWS.SQS.InvalidRequest");
    }

    [Fact]
    public async Task SendAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        var request = new SHSendMessageRequest(TestNamespaceId, "test-queue", "body");
        var result = await sut.SendAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NS.NotFound");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendAsync — SQS queue send success
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_ToSqsQueue_WhenSucceeds_ReturnsSuccess()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueUrlResponse { QueueUrl = "https://sqs.us-east-1.amazonaws.com/123/test-queue" });

        sqsClient.Setup(s => s.SendMessageAsync(It.IsAny<Amazon.SQS.Model.SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse { MessageId = "msg-001" });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        var request = new SHSendMessageRequest(TestNamespaceId, "test-queue", "hello world");
        var result = await sut.SendAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendAsync — SQS queue URL passed directly (no GetQueueUrl call)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_WhenEntityNameIsQueueUrl_SkipsGetQueueUrl()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.SendMessageAsync(It.IsAny<Amazon.SQS.Model.SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageResponse { MessageId = "msg-002" });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        // Entity name is already a full URL
        var request = new SHSendMessageRequest(
            TestNamespaceId,
            "https://sqs.us-east-1.amazonaws.com/123/my-queue",
            "hello");
        var result = await sut.SendAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sqsClient.Verify(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendAsync — SNS publish
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_ToSnsTopicArn_UsesSnsPub()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PublishResponse { MessageId = "sns-msg-001" });

        var sqsClient = new Mock<IAmazonSQS>();

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        var request = new SHSendMessageRequest(
            TestNamespaceId,
            "arn:aws:sns:us-east-1:123:my-topic",
            "event payload");
        var result = await sut.SendAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        snsClient.Verify(s => s.PublishAsync(It.IsAny<PublishRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        sqsClient.Verify(s => s.SendMessageAsync(It.IsAny<Amazon.SQS.Model.SendMessageRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendAsync — SQS error
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_WhenSqsThrows_ReturnsExternalServiceFailure()
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

        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        var request = new SHSendMessageRequest(TestNamespaceId, "bad-queue", "body");
        var result = await sut.SendAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AWS.SQS.SendFailed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendAsync — FIFO queue adds MessageGroupId
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendAsync_ToFifoQueue_AddsFifoAttributes()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        Amazon.SQS.Model.SendMessageRequest? capturedRequest = null;
        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.SendMessageAsync(It.IsAny<Amazon.SQS.Model.SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Amazon.SQS.Model.SendMessageRequest, CancellationToken>((r, _) => capturedRequest = r)
            .ReturnsAsync(new SendMessageResponse { MessageId = "fifo-msg-001" });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        var request = new SHSendMessageRequest(
            TestNamespaceId,
            "https://sqs.us-east-1.amazonaws.com/123/my-queue.fifo",
            "fifo body",
            SessionId: "session-group-1");

        var result = await sut.SendAsync(request, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        capturedRequest.Should().NotBeNull();
        capturedRequest!.MessageGroupId.Should().Be("session-group-1");
        capturedRequest.MessageDeduplicationId.Should().NotBeNullOrEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // SendBatchAsync — empty collection returns success
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SendBatchAsync_EmptyCollection_ReturnsSuccess()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        var result = await sut.SendBatchAsync([], CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendBatchAsync_NullCollection_Throws()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        var act = async () => await sut.SendBatchAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task SendBatchAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        var requests = new[]
        {
            new SHSendMessageRequest(TestNamespaceId, "my-queue", "body-1"),
            new SHSendMessageRequest(TestNamespaceId, "my-queue", "body-2")
        };

        var result = await sut.SendBatchAsync(requests, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NS.NotFound");
    }

    [Fact]
    public async Task SendBatchAsync_WhenSqsBatchSucceeds_ReturnsSuccess()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123/my-queue";

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.SendMessageBatchAsync(It.IsAny<SendMessageBatchRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SendMessageBatchResponse
            {
                Successful = new List<SendMessageBatchResultEntry>
                {
                    new() { Id = "0" },
                    new() { Id = "1" }
                },
                Failed = new List<Amazon.SQS.Model.BatchResultErrorEntry>()
            });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageSender(factory.Object, repo.Object, NullLogger<AwsMessageSender>.Instance);

        var requests = new[]
        {
            new SHSendMessageRequest(TestNamespaceId, queueUrl, "body-1"),
            new SHSendMessageRequest(TestNamespaceId, queueUrl, "body-2")
        };

        var result = await sut.SendBatchAsync(requests, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }
}
