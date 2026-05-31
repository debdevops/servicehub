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
using ServiceHub.Infrastructure.Aws.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Aws;

/// <summary>
/// Tests for <see cref="AwsMessagingProvider"/> and AWS model types.
/// </summary>
public sealed class AwsMessagingProviderTests
{
    private static readonly Guid TestNamespaceId = Guid.NewGuid();

    private static Namespace BuildNamespace() =>
        Namespace.Create(
            "test-aws-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=P;SharedAccessKey=abc=",
            provider: CloudProviderType.Aws,
            awsRegion: "us-east-1").Value;

    private static AwsMessagingProvider BuildProvider(
        IAwsClientFactory? factory = null,
        INamespaceRepository? repo = null,
        AwsMessageReceiver? receiver = null,
        AwsMessageSender? sender = null)
    {
        factory ??= new Mock<IAwsClientFactory>().Object;
        repo ??= new Mock<INamespaceRepository>().Object;

        if (receiver is null)
        {
            var receiverFactory = new Mock<IAwsClientFactory>();
            var receiverRepo = new Mock<INamespaceRepository>();
            receiver = new AwsMessageReceiver(receiverFactory.Object, receiverRepo.Object,
                NullLogger<AwsMessageReceiver>.Instance);
        }

        if (sender is null)
        {
            var senderFactory = new Mock<IAwsClientFactory>();
            var senderRepo = new Mock<INamespaceRepository>();
            sender = new AwsMessageSender(senderFactory.Object, senderRepo.Object,
                NullLogger<AwsMessageSender>.Instance);
        }

        return new AwsMessagingProvider(
            factory, receiver, sender, repo,
            NullLogger<AwsMessagingProvider>.Instance);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Constructor
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullFactory_Throws()
    {
        var receiver = new AwsMessageReceiver(
            new Mock<IAwsClientFactory>().Object,
            new Mock<INamespaceRepository>().Object,
            NullLogger<AwsMessageReceiver>.Instance);
        var sender = new AwsMessageSender(
            new Mock<IAwsClientFactory>().Object,
            new Mock<INamespaceRepository>().Object,
            NullLogger<AwsMessageSender>.Instance);

        var act = () => new AwsMessagingProvider(
            null!, receiver, sender,
            new Mock<INamespaceRepository>().Object,
            NullLogger<AwsMessagingProvider>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientFactory");
    }

    [Fact]
    public void Constructor_ValidArgs_DoesNotThrow()
    {
        var act = () => BuildProvider();
        act.Should().NotThrow();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ProviderType
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProviderType_ReturnsAws()
    {
        var provider = BuildProvider();
        provider.ProviderType.Should().Be(CloudProviderType.Aws);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetMessageReceiver / GetMessageSender
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void GetMessageReceiver_ReturnsReceiverInstance()
    {
        var provider = BuildProvider();
        provider.GetMessageReceiver().Should().NotBeNull();
    }

    [Fact]
    public void GetMessageSender_ReturnsSenderInstance()
    {
        var provider = BuildProvider();
        provider.GetMessageSender().Should().NotBeNull();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ValidateConnectionAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateConnectionAsync_NullNamespace_Throws()
    {
        var provider = BuildProvider();
        var act = async () => await provider.ValidateConnectionAsync(null!, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task ValidateConnectionAsync_WhenSqsListSucceeds_ReturnsSuccess()
    {
        var ns = BuildNamespace();

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string>() });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var provider = BuildProvider(factory: factory.Object);

        var result = await provider.ValidateConnectionAsync(ns, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateConnectionAsync_WhenSqsThrowsAuthError_ReturnsValidationFailure()
    {
        var ns = BuildNamespace();

        var sqsClient = new Mock<IAmazonSQS>();
        var sqsEx = new AmazonSQSException("Invalid token")
        {
            ErrorCode = "InvalidClientTokenId"
        };
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(sqsEx);

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var provider = BuildProvider(factory: factory.Object);

        var result = await provider.ValidateConnectionAsync(ns, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AWS.SQS.AuthFailed");
    }

    [Fact]
    public async Task ValidateConnectionAsync_WhenUnexpectedExceptionOccurs_ReturnsExternalServiceFailure()
    {
        var ns = BuildNamespace();

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Something went wrong"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);

        var provider = BuildProvider(factory: factory.Object);

        var result = await provider.ValidateConnectionAsync(ns, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AWS.SQS.ValidationFailed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListEntitiesAsync — namespace not found
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListEntitiesAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var provider = BuildProvider(repo: repo.Object);

        var result = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("NS.NotFound");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListEntitiesAsync — SQS and SNS success
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListEntitiesAsync_WithQueuesAndTopics_ReturnsAllEntities()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var queueUrl = "https://sqs.us-east-1.amazonaws.com/123/my-queue";

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string> { queueUrl } });

        sqsClient.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "5",
                    ["ApproximateNumberOfMessagesNotVisible"] = "2"
                }
            });

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse
            {
                Topics = new List<Topic> { new() { TopicArn = "arn:aws:sns:us-east-1:123:my-topic" } }
            });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var result = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value.Should().Contain(e => e.EntityType == "Queue" && e.Name == "my-queue");
        result.Value.Should().Contain(e => e.EntityType == "SNS Topic");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListEntitiesAsync — SQS error
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListEntitiesAsync_WhenSqsThrows_ReturnsFailure()
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

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var result = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AWS.SQS.ListFailed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // GetSnsFanoutMapAsync
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetSnsFanoutMapAsync_WhenNamespaceNotFound_ReturnsFailure()
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("NS.NotFound", "Not found")));

        var provider = BuildProvider(repo: repo.Object);

        var result = await provider.GetSnsFanoutMapAsync(TestNamespaceId, "arn:aws:sns:us-east-1:123:topic", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task GetSnsFanoutMapAsync_WhenSnsSucceeds_ReturnsFanoutMap()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var topicArn = "arn:aws:sns:us-east-1:123:my-topic";

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListSubscriptionsByTopicAsync(topicArn, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListSubscriptionsByTopicResponse
            {
                Subscriptions = new List<Amazon.SimpleNotificationService.Model.Subscription>
                {
                    new() { SubscriptionArn = "arn:aws:sns:us-east-1:123:my-topic:sub-001", Protocol = "sqs", Endpoint = "arn:aws:sqs:us-east-1:123:my-queue" },
                    new() { SubscriptionArn = "PendingConfirmation", Protocol = "email", Endpoint = "user@example.com" }
                }
            });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var result = await provider.GetSnsFanoutMapAsync(TestNamespaceId, topicArn, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.TopicArn.Should().Be(topicArn);
        result.Value.Subscriptions.Should().HaveCount(2);
        result.Value.Subscriptions.Should().Contain(s => s.Status == "Confirmed");
        result.Value.Subscriptions.Should().Contain(s => s.Status == "PendingConfirmation");
    }

    [Fact]
    public async Task GetSnsFanoutMapAsync_WhenSnsThrows_ReturnsFailure()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListSubscriptionsByTopicAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("SNS error"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var result = await provider.GetSnsFanoutMapAsync(TestNamespaceId, "arn:aws:sns:us-east-1:123:topic", CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        result.Error.Code.Should().Be("AWS.SNS.FanoutMapFailed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // AWS model tests — SnsFanoutMap and SnsSubscriptionStatus
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void SnsFanoutMap_Constructor_SetsProperties()
    {
        var subs = new List<SnsSubscriptionStatus>
        {
            new("arn:aws:sns:us-east-1:123:topic:sub-001", "sqs", "arn:aws:sqs:us-east-1:123:queue", "Confirmed")
        };
        var map = new SnsFanoutMap("arn:aws:sns:us-east-1:123:my-topic", subs);

        map.TopicArn.Should().Be("arn:aws:sns:us-east-1:123:my-topic");
        map.Subscriptions.Should().HaveCount(1);
    }

    [Fact]
    public void SnsSubscriptionStatus_Constructor_SetsAllProperties()
    {
        var sub = new SnsSubscriptionStatus(
            "arn:aws:sns:us-east-1:123:topic:sub-001",
            "sqs",
            "arn:aws:sqs:us-east-1:123:my-queue",
            "Confirmed");

        sub.SubscriptionArn.Should().Be("arn:aws:sns:us-east-1:123:topic:sub-001");
        sub.Protocol.Should().Be("sqs");
        sub.Endpoint.Should().Be("arn:aws:sqs:us-east-1:123:my-queue");
        sub.Status.Should().Be("Confirmed");
    }

    [Fact]
    public void SnsSubscriptionStatus_RecordEquality_WorksCorrectly()
    {
        var sub1 = new SnsSubscriptionStatus("arn", "sqs", "endpoint", "Confirmed");
        var sub2 = new SnsSubscriptionStatus("arn", "sqs", "endpoint", "Confirmed");
        var sub3 = new SnsSubscriptionStatus("arn-different", "sqs", "endpoint", "Confirmed");

        sub1.Should().Be(sub2);
        sub1.Should().NotBe(sub3);
    }
}
