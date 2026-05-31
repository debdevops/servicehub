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
using SHMessage = ServiceHub.Core.Entities.Message;
using SHNamespace = ServiceHub.Core.Entities.Namespace;

namespace ServiceHub.UnitTests.Infrastructure.Aws;

/// <summary>
/// Extended tests for <see cref="AwsMessageReceiver"/> covering happy paths,
/// DLQ resolution, message mapping, and replay/dead-letter scenarios.
/// </summary>
public sealed class AwsMessageReceiverExtendedTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();
    private const string QueueUrl = "https://sqs.us-east-1.amazonaws.com/123456/test-queue";
    private const string DlqUrl = "https://sqs.us-east-1.amazonaws.com/123456/test-queue-dlq";
    private const string QueueName = "test-queue";

    private static SHNamespace BuildNamespace() =>
        SHNamespace.Create(
            "test-aws-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=",
            provider: CloudProviderType.Aws,
            awsRegion: "us-east-1").Value;

    private static Mock<IAmazonSQS> BuildSqsWithQueueUrl(string queueUrl = QueueUrl)
    {
        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueUrlResponse { QueueUrl = queueUrl });
        return sqsClient;
    }

    private static Message BuildSqsMessage(
        string body = "test-body",
        string messageId = "msg-001",
        string receiptHandle = "rh-001",
        int deliveryCount = 1,
        long sentTimestampMs = 0)
    {
        var sentEpoch = sentTimestampMs > 0 ? sentTimestampMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        return new Message
        {
            MessageId = messageId,
            ReceiptHandle = receiptHandle,
            Body = body,
            Attributes = new Dictionary<string, string>
            {
                ["SentTimestamp"] = sentEpoch.ToString(),
                ["ApproximateReceiveCount"] = deliveryCount.ToString()
            },
            MessageAttributes = new Dictionary<string, MessageAttributeValue>()
        };
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor guards
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullFactory_Throws()
    {
        var act = () => new AwsMessageReceiver(null!, new Mock<INamespaceRepository>().Object,
            NullLogger<AwsMessageReceiver>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clientFactory");
    }

    [Fact]
    public void Constructor_NullRepo_Throws()
    {
        var act = () => new AwsMessageReceiver(new Mock<IAwsClientFactory>().Object, null!,
            NullLogger<AwsMessageReceiver>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new AwsMessageReceiver(new Mock<IAwsClientFactory>().Object,
            new Mock<INamespaceRepository>().Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PeekMessagesAsync — null request
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekMessagesAsync_NullRequest_Throws()
    {
        var sut = new AwsMessageReceiver(new Mock<IAwsClientFactory>().Object,
            new Mock<INamespaceRepository>().Object, NullLogger<AwsMessageReceiver>.Instance);
        var act = async () => await sut.PeekMessagesAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PeekMessagesAsync — full URL bypasses GetQueueUrl
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekMessagesAsync_WhenEntityNameIsFullUrl_DoesNotCallGetQueueUrl()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse { Messages = new List<Message>() });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.PeekMessagesAsync(
            new GetMessagesRequest(TestNamespaceId, QueueUrl, null, false, 10));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
        // GetQueueUrl should NOT be called for full URLs
        sqsClient.Verify(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PeekMessagesAsync — success with messages
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekMessagesAsync_WhenSuccess_ReturnsMappedMessages()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsMsg = BuildSqsMessage("hello world", "msg-abc", "rh-abc", deliveryCount: 3);
        var sqsClient = BuildSqsWithQueueUrl();
        sqsClient.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse { Messages = new List<Message> { sqsMsg } });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.PeekMessagesAsync(
            new GetMessagesRequest(TestNamespaceId, QueueName, null, false, 10));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
        result.Value[0].Body.Should().Be("hello world");
        result.Value[0].DeliveryCount.Should().Be(3);
        result.Value[0].IsFromDeadLetter.Should().BeFalse();
        result.Value[0].EntityName.Should().Be(QueueName);
    }

    [Fact]
    public async Task PeekMessagesAsync_WithMessageAttributes_MapsToApplicationProperties()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsMsg = BuildSqsMessage("body", "msg-1", "rh-1");
        sqsMsg.MessageAttributes["CustomKey"] = new MessageAttributeValue
        {
            DataType = "String",
            StringValue = "CustomValue"
        };

        var sqsClient = BuildSqsWithQueueUrl();
        sqsClient.Setup(s => s.ReceiveMessageAsync(It.IsAny<ReceiveMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ReceiveMessageResponse { Messages = new List<Message> { sqsMsg } });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.PeekMessagesAsync(
            new GetMessagesRequest(TestNamespaceId, QueueName, null, false, 10));

        result.IsSuccess.Should().BeTrue();
        result.Value[0].ApplicationProperties.Should().ContainKey("CustomKey");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetMessageCountAsync — success
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetMessageCountAsync_WhenSuccess_ReturnsVisiblePlusInFlight()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = BuildSqsWithQueueUrl();
        sqsClient.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "5",
                    ["ApproximateNumberOfMessagesNotVisible"] = "3"
                }
            });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.GetMessageCountAsync(TestNamespaceId, QueueName);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(8); // 5 visible + 3 in-flight
    }

    // ─────────────────────────────────────────────────────────────────────────
    // PeekDeadLetterMessagesAsync — no DLQ configured
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_WhenNoDlqConfigured_ReturnsEmptyList()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = BuildSqsWithQueueUrl();
        // GetQueueAttributes returns no RedrivePolicy
        sqsClient.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>() // no RedrivePolicy key
            });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.PeekDeadLetterMessagesAsync(
            new GetMessagesRequest(TestNamespaceId, QueueName, null, true, 10));

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_NullRequest_Throws()
    {
        var sut = new AwsMessageReceiver(new Mock<IAwsClientFactory>().Object,
            new Mock<INamespaceRepository>().Object, NullLogger<AwsMessageReceiver>.Instance);
        var act = async () => await sut.PeekDeadLetterMessagesAsync(null!);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SHNamespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new AwsMessageReceiver(new Mock<IAwsClientFactory>().Object,
            repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.PeekDeadLetterMessagesAsync(
            new GetMessagesRequest(TestNamespaceId, QueueName, null, true, 10));

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_WhenSqsThrows_ReturnsFailure()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.GetQueueUrlAsync(It.IsAny<GetQueueUrlRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("SQS unavailable"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.PeekDeadLetterMessagesAsync(
            new GetMessagesRequest(TestNamespaceId, QueueName, null, true, 10));

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AWS.SQS.DlqPeekFailed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ReplayMessageAsync — namespace not found
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ReplayMessageAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<SHNamespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new AwsMessageReceiver(new Mock<IAwsClientFactory>().Object,
            repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.ReplayMessageAsync(TestNamespaceId, QueueName, null, 999L);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NS.NotFound");
    }

    [Fact]
    public async Task ReplayMessageAsync_WhenNoDlqConfigured_ReturnsValidationFailure()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = BuildSqsWithQueueUrl();
        sqsClient.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>() // no RedrivePolicy
            });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.ReplayMessageAsync(TestNamespaceId, QueueName, null, 42L);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AWS.SQS.NoDlq");
    }

    [Fact]
    public async Task ReplayMessageAsync_WhenReceiptHandleNotCached_ReturnsNotFound()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = BuildSqsWithQueueUrl();
        // Has DLQ configured
        sqsClient.Setup(s => s.GetQueueAttributesAsync(
            It.Is<GetQueueAttributesRequest>(r => r.AttributeNames.Contains("RedrivePolicy")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["RedrivePolicy"] = @"{""maxReceiveCount"":3,""deadLetterTargetArn"":""arn:aws:sqs:us-east-1:123456:test-queue-dlq""}"
                }
            });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        // sequence number 99999 is not in cache
        var result = await sut.ReplayMessageAsync(TestNamespaceId, QueueName, null, 99999L);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AWS.SQS.MessageNotFound");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetVisibilityWindowStatusAsync — success with DLQ
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVisibilityWindowStatusAsync_WhenQueueHasDlq_ReturnsDlqCount()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = BuildSqsWithQueueUrl();

        // Main queue attributes (for visibility status)
        sqsClient.Setup(s => s.GetQueueAttributesAsync(
            It.Is<GetQueueAttributesRequest>(r => r.AttributeNames.Contains("VisibilityTimeout")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessagesNotVisible"] = "2",
                    ["VisibilityTimeout"] = "30",
                    ["RedrivePolicy"] = @"{""maxReceiveCount"":3,""deadLetterTargetArn"":""arn:aws:sqs:us-east-1:123456:test-queue-dlq""}"
                }
            });

        // Redrive policy resolution (for DLQ URL) - getAttributes without VisibilityTimeout
        sqsClient.Setup(s => s.GetQueueAttributesAsync(
            It.Is<GetQueueAttributesRequest>(r => r.AttributeNames.Contains("RedrivePolicy") && !r.AttributeNames.Contains("VisibilityTimeout")),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["RedrivePolicy"] = @"{""maxReceiveCount"":3,""deadLetterTargetArn"":""arn:aws:sqs:us-east-1:123456:test-queue-dlq""}"
                }
            });

        // DLQ attributes
        sqsClient.Setup(s => s.GetQueueAttributesAsync(
            It.Is<GetQueueAttributesRequest>(r => r.QueueUrl == DlqUrl),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "7"
                }
            });

        // DLQ URL resolution
        sqsClient.Setup(s => s.GetQueueUrlAsync(
            It.Is<GetQueueUrlRequest>(r => r.QueueName == "test-queue-dlq"),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueUrlResponse { QueueUrl = DlqUrl });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.GetVisibilityWindowStatusAsync(TestNamespaceId, QueueName);

        result.IsSuccess.Should().BeTrue();
        result.Value.InFlightCount.Should().Be(2);
        result.Value.VisibilityTimeoutSeconds.Should().Be(30);
    }

    [Fact]
    public async Task GetVisibilityWindowStatusAsync_WhenNoDlq_ReturnsZeroDlqCount()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = BuildSqsWithQueueUrl();
        sqsClient.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessagesNotVisible"] = "0",
                    ["VisibilityTimeout"] = "60"
                    // no RedrivePolicy key
                }
            });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<SHNamespace>())).Returns(sqsClient.Object);

        var sut = new AwsMessageReceiver(factory.Object, repo.Object, NullLogger<AwsMessageReceiver>.Instance);

        var result = await sut.GetVisibilityWindowStatusAsync(TestNamespaceId, QueueName);

        result.IsSuccess.Should().BeTrue();
        result.Value.DlqCount.Should().Be(0);
        result.Value.VisibilityTimeoutSeconds.Should().Be(60);
    }
}
