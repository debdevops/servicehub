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

namespace ServiceHub.UnitTests.Infrastructure.Aws;

/// <summary>
/// Tests for <see cref="AwsDlqDetector"/>.
/// </summary>
public sealed class AwsDlqDetectorTests
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
        var act = () => new AwsDlqDetector(null!, repo.Object, NullLogger<AwsDlqDetector>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clientFactory");
    }

    [Fact]
    public void Constructor_NullRepo_Throws()
    {
        var factory = new Mock<IAwsClientFactory>();
        var act = () => new AwsDlqDetector(factory.Object, null!, NullLogger<AwsDlqDetector>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var act = () => new AwsDlqDetector(factory.Object, repo.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public void Constructor_ValidArgs_DoesNotThrow()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        var act = () => new AwsDlqDetector(factory.Object, repo.Object, NullLogger<AwsDlqDetector>.Instance);
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListDeadLetterQueuesAsync — namespace not found
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListDeadLetterQueuesAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var factory = new Mock<IAwsClientFactory>();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var sut = new AwsDlqDetector(factory.Object, repo.Object, NullLogger<AwsDlqDetector>.Instance);

        var result = await sut.ListDeadLetterQueuesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NS.NotFound");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListDeadLetterQueuesAsync — SQS throws
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListDeadLetterQueuesAsync_WhenSqsThrows_ReturnsFailure()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Connection refused"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsDlqDetector(factory.Object, repo.Object, NullLogger<AwsDlqDetector>.Instance);

        var result = await sut.ListDeadLetterQueuesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AWS.SQS.DlqListFailed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListDeadLetterQueuesAsync — empty queue list
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListDeadLetterQueuesAsync_WithNoQueues_ReturnsEmptyDictionary()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string>() });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsDlqDetector(factory.Object, repo.Object, NullLogger<AwsDlqDetector>.Instance);

        var result = await sut.ListDeadLetterQueuesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListDeadLetterQueuesAsync — queues without redrive policy are skipped
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListDeadLetterQueuesAsync_QueuesWithNoRedrivePolicy_AreExcluded()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse
            {
                QueueUrls = new List<string> { "https://sqs.us-east-1.amazonaws.com/123/no-dlq-queue" }
            });

        // Return attributes without RedrivePolicy
        sqsClient.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>() // no RedrivePolicy key
            });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsDlqDetector(factory.Object, repo.Object, NullLogger<AwsDlqDetector>.Instance);

        var result = await sut.ListDeadLetterQueuesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListDeadLetterQueuesAsync — queue with valid redrive policy is included
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListDeadLetterQueuesAsync_QueueWithRedrivePolicy_IsIncluded()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123/my-queue";
        var dlqArn = "arn:aws:sqs:us-east-1:123:my-queue-dlq";
        var redrivePolicy = $"{{\"deadLetterTargetArn\":\"{dlqArn}\",\"maxReceiveCount\":5}}";

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse
            {
                QueueUrls = new List<string> { queueUrl }
            });

        sqsClient.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string> { ["RedrivePolicy"] = redrivePolicy }
            });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsDlqDetector(factory.Object, repo.Object, NullLogger<AwsDlqDetector>.Instance);

        var result = await sut.ListDeadLetterQueuesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().ContainKey("my-queue");
        result.Value["my-queue"].Should().Be(dlqArn);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListDeadLetterQueuesAsync — SQS error per-queue is logged and skipped
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListDeadLetterQueuesAsync_SqsErrorOnAttributesFetch_SkipsQueue()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123/problem-queue";

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string> { queueUrl } });

        // Per-queue SQS error
        sqsClient.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Access Denied"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var sut = new AwsDlqDetector(factory.Object, repo.Object, NullLogger<AwsDlqDetector>.Instance);

        // Should succeed but skip the problematic queue
        var result = await sut.ListDeadLetterQueuesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }
}
