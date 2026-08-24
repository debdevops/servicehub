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

    [Fact]
    public void Capabilities_ReflectsAwsConstraints()
    {
        var capabilities = BuildProvider().Capabilities;

        capabilities.SupportsMessageCounts.Should().BeTrue();
        capabilities.SupportsManualDeadLetter.Should().BeTrue();
        capabilities.SupportsPurge.Should().BeTrue();
        capabilities.SupportsScheduledMessages.Should().BeFalse();
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
        snsClient.Setup(s => s.ListTopicsAsync(It.IsAny<ListTopicsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse
            {
                Topics = new List<Topic> { new() { TopicArn = "arn:aws:sns:us-east-1:123:my-topic" } }
            });
        snsClient.Setup(s => s.ListSubscriptionsByTopicAsync(It.IsAny<ListSubscriptionsByTopicRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListSubscriptionsByTopicResponse { Subscriptions = new List<Subscription>() });

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
    // ListEntitiesForReconciliationAsync — partial AWS listing failures (release-gate fix)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_OneQueueAttributesFail_MarksOnlyThatQueueIncomplete()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        const string goodQueueUrl = "https://sqs.us-east-1.amazonaws.com/123/good-queue";
        const string badQueueUrl = "https://sqs.us-east-1.amazonaws.com/123/bad-queue";

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string> { goodQueueUrl, badQueueUrl } });

        sqsClient.Setup(s => s.GetQueueAttributesAsync(
                It.Is<GetQueueAttributesRequest>(r => r.QueueUrl == goodQueueUrl), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "0",
                    ["ApproximateNumberOfMessagesNotVisible"] = "0"
                }
            });
        sqsClient.Setup(s => s.GetQueueAttributesAsync(
                It.Is<GetQueueAttributesRequest>(r => r.QueueUrl == badQueueUrl), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Throttled"));

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(It.IsAny<ListTopicsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse { Topics = new List<Topic>() });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var scanResult = await provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);

        scanResult.IsSuccess.Should().BeTrue();
        scanResult.Value.IncompleteQueueNames.Should().ContainSingle("bad-queue");
        scanResult.Value.Entities.Should().ContainSingle(e => e.EntityType == "Queue" && e.Name == "good-queue");
        scanResult.Value.Entities.Should().NotContain(e => e.Name == "bad-queue");
        scanResult.Value.SnsListingFailed.Should().BeFalse();

        // ListEntitiesAsync's own contract (used by every other caller) is unaffected — same
        // successfully-collected entities, silently dropping the failed queue as it always did.
        var plainResult = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);
        plainResult.IsSuccess.Should().BeTrue();
        plainResult.Value.Should().ContainSingle(e => e.Name == "good-queue");
    }

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_SnsListTopicsThrows_SetsSnsListingFailed()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string>() });

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(It.IsAny<ListTopicsRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSimpleNotificationServiceException("Throttled"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var scanResult = await provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);

        scanResult.IsSuccess.Should().BeTrue();
        scanResult.Value.SnsListingFailed.Should().BeTrue();
        scanResult.Value.IncompleteTopicNames.Should().BeEmpty();

        // The overall list call still succeeds (SQS-only outcome), matching today's behavior.
        var plainResult = await provider.ListEntitiesAsync(TestNamespaceId, CancellationToken.None);
        plainResult.IsSuccess.Should().BeTrue();
        plainResult.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_OneTopicSubscriptionListingFails_MarksOnlyThatTopicIncomplete()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        const string goodTopicArn = "arn:aws:sns:us-east-1:123:billing-topic";
        const string badTopicArn = "arn:aws:sns:us-east-1:123:orders-topic";

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string>() });

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(It.IsAny<ListTopicsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse
            {
                Topics = new List<Topic> { new() { TopicArn = goodTopicArn }, new() { TopicArn = badTopicArn } }
            });
        snsClient.Setup(s => s.ListSubscriptionsByTopicAsync(
                It.Is<ListSubscriptionsByTopicRequest>(r => r.TopicArn == goodTopicArn), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListSubscriptionsByTopicResponse { Subscriptions = new List<Subscription>() });
        snsClient.Setup(s => s.ListSubscriptionsByTopicAsync(
                It.Is<ListSubscriptionsByTopicRequest>(r => r.TopicArn == badTopicArn), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSimpleNotificationServiceException("Throttled"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var scanResult = await provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);

        scanResult.IsSuccess.Should().BeTrue();
        scanResult.Value.IncompleteTopicNames.Should().ContainSingle("orders-topic");
        scanResult.Value.SnsListingFailed.Should().BeFalse();
        // Both topics still appear — only the subscription listing under one of them failed.
        scanResult.Value.Entities.Should().Contain(e => e.EntityType == "SNS Topic" && e.Name == "billing-topic");
        scanResult.Value.Entities.Should().Contain(e => e.EntityType == "SNS Topic" && e.Name == "orders-topic");
    }

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_SourceQueueSubscriptionListingThrowsCancellation_Propagates()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        const string topicArn = "arn:aws:sns:us-east-1:123:orders-topic";

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string>() });

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(It.IsAny<ListTopicsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse { Topics = new List<Topic> { new() { TopicArn = topicArn } } });
        snsClient.Setup(s => s.ListSubscriptionsByTopicAsync(
                It.IsAny<ListSubscriptionsByTopicRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        // Cancellation must propagate as a genuine cancellation, not be swallowed into an
        // "incomplete topic" marker that lets the scan report a false success.
        var act = () => provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_RedriveTargetAttributesFail_MarksSourceQueueIncompleteToo()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        const string sourceQueueUrl = "https://sqs.us-east-1.amazonaws.com/123/orders";
        const string dlqQueueUrl = "https://sqs.us-east-1.amazonaws.com/123/orders-dlq";
        const string redrivePolicy = @"{""maxReceiveCount"":3,""deadLetterTargetArn"":""arn:aws:sqs:us-east-1:123:orders-dlq""}";

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string> { sourceQueueUrl, dlqQueueUrl } });

        sqsClient.Setup(s => s.GetQueueAttributesAsync(
                It.Is<GetQueueAttributesRequest>(r => r.QueueUrl == sourceQueueUrl), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "0",
                    ["ApproximateNumberOfMessagesNotVisible"] = "0",
                    ["RedrivePolicy"] = redrivePolicy
                }
            });
        // The DLQ target's own attribute fetch fails — its live count can never be confirmed.
        sqsClient.Setup(s => s.GetQueueAttributesAsync(
                It.Is<GetQueueAttributesRequest>(r => r.QueueUrl == dlqQueueUrl), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Throttled"));

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(It.IsAny<ListTopicsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse { Topics = new List<Topic>() });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var scanResult = await provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);

        scanResult.IsSuccess.Should().BeTrue();
        // Both the failed target and the source that redrives into it must be treated as
        // unconfirmed — otherwise DlqMonitorService would trust the source's fallback
        // DeadLetterCount of 0 as a confirmed-empty DLQ and reconcile it as vanished.
        scanResult.Value.IncompleteQueueNames.Should().Contain("orders-dlq");
        scanResult.Value.IncompleteQueueNames.Should().Contain("orders");

        var sourceEntity = scanResult.Value.Entities.Should().ContainSingle(e => e.Name == "orders").Which;
        sourceEntity.DeadLetterCount.Should().Be(0);
        sourceEntity.DeadLetterTargetName.Should().Be("orders-dlq");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ListEntitiesForReconciliationAsync — pagination (release-gate fix)
    //
    // AWS truncates ListQueues/ListTopics/ListSubscriptionsByTopic to a single page unless the
    // NextToken is threaded through follow-up requests. Before this fix, an entity that only
    // existed on page 2+ was indistinguishable from a genuinely deleted entity — reconciliation
    // (DlqMonitorServiceTests) would mark its stale Active DLQ records VanishedExternally even
    // though the entity was still live. These tests prove pagination is fully consumed at the
    // AwsMessagingProvider layer, which is the precondition DlqMonitorServiceTests' existing
    // reconciliation-safety tests rely on.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_MultipleSqsQueuePages_AllQueuesAcrossAllPagesAppearAsEntities()
    {
        const string page1Url = "https://sqs.us-east-1.amazonaws.com/123/queue-page1";
        const string page2Url = "https://sqs.us-east-1.amazonaws.com/123/queue-page2";
        const string page3Url = "https://sqs.us-east-1.amazonaws.com/123/queue-page3-final";

        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(
                It.Is<ListQueuesRequest>(r => r.MaxResults == 1000 && r.NextToken == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string> { page1Url }, NextToken = "token-1" });
        sqsClient.Setup(s => s.ListQueuesAsync(
                It.Is<ListQueuesRequest>(r => r.MaxResults == 1000 && r.NextToken == "token-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string> { page2Url }, NextToken = "token-2" });
        sqsClient.Setup(s => s.ListQueuesAsync(
                It.Is<ListQueuesRequest>(r => r.MaxResults == 1000 && r.NextToken == "token-2"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string> { page3Url }, NextToken = null });

        sqsClient.Setup(s => s.GetQueueAttributesAsync(It.IsAny<GetQueueAttributesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string>
                {
                    ["ApproximateNumberOfMessages"] = "0",
                    ["ApproximateNumberOfMessagesNotVisible"] = "0"
                }
            });

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(It.IsAny<ListTopicsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse { Topics = new List<Topic>() });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var scanResult = await provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);

        scanResult.IsSuccess.Should().BeTrue();
        scanResult.Value.Entities.Should().HaveCount(3);
        scanResult.Value.Entities.Should().Contain(e => e.Name == "queue-page1");
        scanResult.Value.Entities.Should().Contain(e => e.Name == "queue-page2");
        scanResult.Value.Entities.Should().Contain(e => e.Name == "queue-page3-final");
        scanResult.Value.IncompleteQueueNames.Should().BeEmpty();
        sqsClient.Verify(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_MultipleSnsTopicPages_AllTopicsAcrossAllPagesAppearAsEntities()
    {
        const string page1TopicArn = "arn:aws:sns:us-east-1:123:topic-page1";
        const string page2TopicArn = "arn:aws:sns:us-east-1:123:topic-page2-final";

        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string>() });

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(
                It.Is<ListTopicsRequest>(r => r.NextToken == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse
            {
                Topics = new List<Topic> { new() { TopicArn = page1TopicArn } },
                NextToken = "sns-token-1"
            });
        snsClient.Setup(s => s.ListTopicsAsync(
                It.Is<ListTopicsRequest>(r => r.NextToken == "sns-token-1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse
            {
                Topics = new List<Topic> { new() { TopicArn = page2TopicArn } },
                NextToken = null
            });
        snsClient.Setup(s => s.ListSubscriptionsByTopicAsync(
                It.IsAny<ListSubscriptionsByTopicRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListSubscriptionsByTopicResponse { Subscriptions = new List<Subscription>() });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var scanResult = await provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);

        scanResult.IsSuccess.Should().BeTrue();
        scanResult.Value.Entities.Should().Contain(e => e.EntityType == "SNS Topic" && e.Name == "topic-page1");
        scanResult.Value.Entities.Should().Contain(e => e.EntityType == "SNS Topic" && e.Name == "topic-page2-final");
        scanResult.Value.SnsListingFailed.Should().BeFalse();
        snsClient.Verify(s => s.ListTopicsAsync(It.IsAny<ListTopicsRequest>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_MultipleSnsSubscriptionPagesForOneTopic_AllSubscriptionsAppearAsEntities()
    {
        const string topicArn = "arn:aws:sns:us-east-1:123:fanout-topic";

        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string>() });

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(It.IsAny<ListTopicsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse { Topics = new List<Topic> { new() { TopicArn = topicArn } } });

        snsClient.Setup(s => s.ListSubscriptionsByTopicAsync(
                It.Is<ListSubscriptionsByTopicRequest>(r => r.TopicArn == topicArn && r.NextToken == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListSubscriptionsByTopicResponse
            {
                Subscriptions = new List<Subscription> { new() { Endpoint = "arn:aws:sqs:us-east-1:123:sub-page1", Protocol = "sqs" } },
                NextToken = "sub-token-1"
            });
        snsClient.Setup(s => s.ListSubscriptionsByTopicAsync(
                It.Is<ListSubscriptionsByTopicRequest>(r => r.TopicArn == topicArn && r.NextToken == "sub-token-1"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListSubscriptionsByTopicResponse
            {
                Subscriptions = new List<Subscription> { new() { Endpoint = "arn:aws:sqs:us-east-1:123:sub-page2-final", Protocol = "sqs" } },
                NextToken = null
            });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var scanResult = await provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);

        scanResult.IsSuccess.Should().BeTrue();
        scanResult.Value.Entities.Should().Contain(e => e.EntityType == "Subscription" && e.Name == "fanout-topic/sub-page1");
        scanResult.Value.Entities.Should().Contain(e => e.EntityType == "Subscription" && e.Name == "fanout-topic/sub-page2-final");
        scanResult.Value.IncompleteTopicNames.Should().BeEmpty();
        snsClient.Verify(
            s => s.ListSubscriptionsByTopicAsync(It.IsAny<ListSubscriptionsByTopicRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_QueueAttributesFailOnSecondPage_OnlyThatQueueIncomplete_ThirdPageStillConsumed()
    {
        const string page1Url = "https://sqs.us-east-1.amazonaws.com/123/good-page1";
        const string page2BadUrl = "https://sqs.us-east-1.amazonaws.com/123/bad-page2";
        const string page3Url = "https://sqs.us-east-1.amazonaws.com/123/good-page3-final";

        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(
                It.Is<ListQueuesRequest>(r => r.NextToken == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string> { page1Url }, NextToken = "t1" });
        sqsClient.Setup(s => s.ListQueuesAsync(
                It.Is<ListQueuesRequest>(r => r.NextToken == "t1"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string> { page2BadUrl }, NextToken = "t2" });
        sqsClient.Setup(s => s.ListQueuesAsync(
                It.Is<ListQueuesRequest>(r => r.NextToken == "t2"), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string> { page3Url }, NextToken = null });

        sqsClient.Setup(s => s.GetQueueAttributesAsync(
                It.Is<GetQueueAttributesRequest>(r => r.QueueUrl == page1Url), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string> { ["ApproximateNumberOfMessages"] = "0", ["ApproximateNumberOfMessagesNotVisible"] = "0" }
            });
        sqsClient.Setup(s => s.GetQueueAttributesAsync(
                It.Is<GetQueueAttributesRequest>(r => r.QueueUrl == page2BadUrl), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("Throttled"));
        sqsClient.Setup(s => s.GetQueueAttributesAsync(
                It.Is<GetQueueAttributesRequest>(r => r.QueueUrl == page3Url), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GetQueueAttributesResponse
            {
                Attributes = new Dictionary<string, string> { ["ApproximateNumberOfMessages"] = "0", ["ApproximateNumberOfMessagesNotVisible"] = "0" }
            });

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(It.IsAny<ListTopicsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse { Topics = new List<Topic>() });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var scanResult = await provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);

        scanResult.IsSuccess.Should().BeTrue();
        scanResult.Value.IncompleteQueueNames.Should().ContainSingle("bad-page2");
        scanResult.Value.Entities.Should().Contain(e => e.Name == "good-page1");
        scanResult.Value.Entities.Should().Contain(e => e.Name == "good-page3-final");
        scanResult.Value.Entities.Should().NotContain(e => e.Name == "bad-page2");
    }

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_SnsTopicListingFailsOnSecondPage_SetsSnsListingFailed()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string>() });

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(
                It.Is<ListTopicsRequest>(r => r.NextToken == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse
            {
                Topics = new List<Topic> { new() { TopicArn = "arn:aws:sns:us-east-1:123:topic-a" } },
                NextToken = "sns-token-1"
            });
        snsClient.Setup(s => s.ListTopicsAsync(
                It.Is<ListTopicsRequest>(r => r.NextToken == "sns-token-1"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSimpleNotificationServiceException("Throttled"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var scanResult = await provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);

        scanResult.IsSuccess.Should().BeTrue();
        scanResult.Value.SnsListingFailed.Should().BeTrue();
        // A page-1 topic collected before the page-2 failure must not leak through as "confirmed" —
        // the whole SNS side of the scan is unconfirmed once pagination is interrupted.
        scanResult.Value.Entities.Should().NotContain(e => e.EntityType == "SNS Topic");
    }

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_SubscriptionListingFailsOnSecondPage_MarksOnlyThatTopicIncomplete()
    {
        const string topicArn = "arn:aws:sns:us-east-1:123:flaky-topic";

        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string>() });

        var snsClient = new Mock<IAmazonSimpleNotificationService>();
        snsClient.Setup(s => s.ListTopicsAsync(It.IsAny<ListTopicsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListTopicsResponse { Topics = new List<Topic> { new() { TopicArn = topicArn } } });

        snsClient.Setup(s => s.ListSubscriptionsByTopicAsync(
                It.Is<ListSubscriptionsByTopicRequest>(r => r.TopicArn == topicArn && r.NextToken == null),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListSubscriptionsByTopicResponse
            {
                Subscriptions = new List<Subscription> { new() { Endpoint = "arn:aws:sqs:us-east-1:123:sub-a", Protocol = "sqs" } },
                NextToken = "sub-token-1"
            });
        snsClient.Setup(s => s.ListSubscriptionsByTopicAsync(
                It.Is<ListSubscriptionsByTopicRequest>(r => r.TopicArn == topicArn && r.NextToken == "sub-token-1"),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSimpleNotificationServiceException("Throttled"));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        var scanResult = await provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);

        scanResult.IsSuccess.Should().BeTrue();
        scanResult.Value.IncompleteTopicNames.Should().ContainSingle("flaky-topic");
        scanResult.Value.SnsListingFailed.Should().BeFalse();
        // The topic itself was listed fine — only its subscriptions are unconfirmed — so the
        // partial subscription set collected before the failure must not leak through either.
        scanResult.Value.Entities.Should().Contain(e => e.EntityType == "SNS Topic" && e.Name == "flaky-topic");
        scanResult.Value.Entities.Should().NotContain(e => e.EntityType == "Subscription");
    }

    [Fact]
    public async Task ListEntitiesForReconciliationAsync_CancelledDuringSecondQueuePage_PropagatesCancellation_DoesNotReturnSuccess()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.Setup(s => s.ListQueuesAsync(
                It.Is<ListQueuesRequest>(r => r.NextToken == null), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string> { "q1" }, NextToken = "t1" });
        sqsClient.Setup(s => s.ListQueuesAsync(
                It.Is<ListQueuesRequest>(r => r.NextToken == "t1"), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var snsClient = new Mock<IAmazonSimpleNotificationService>();

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        factory.Setup(f => f.GetSnsClient(It.IsAny<Namespace>())).Returns(snsClient.Object);

        var provider = BuildProvider(factory: factory.Object, repo: repo.Object);

        // An interrupted pagination sweep must never surface as a successful (and therefore
        // reconciliation-eligible) scan result — it must propagate as a genuine cancellation.
        var act = async () => await provider.ListEntitiesForReconciliationAsync(TestNamespaceId, CancellationToken.None);
        await act.Should().ThrowAsync<OperationCanceledException>();
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
    // ListEntitiesAsync — factory configuration error (fail-closed credentials)
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ListEntitiesAsync_WhenFactoryFailsClosed_ReturnsFailureInsteadOfThrowing()
    {
        var ns = BuildNamespace();
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetByIdAsync(TestNamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(ns));

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>()))
            .Throws(new InvalidOperationException("Namespace has an unsupported AWS auth type."));

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

    // ─────────────────────────────────────────────────────────────────────────
    // Resilience pipeline coverage on provider-level SDK calls
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ValidateConnectionAsync_TransientSqsError_IsRetriedAndSucceeds()
    {
        var sqsClient = new Mock<IAmazonSQS>();
        sqsClient.SetupSequence(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new AmazonSQSException("throttled") { StatusCode = System.Net.HttpStatusCode.ServiceUnavailable })
            .ReturnsAsync(new ListQueuesResponse { QueueUrls = new List<string>() });

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        var provider = BuildProvider(factory: factory.Object);

        var result = await provider.ValidateConnectionAsync(BuildNamespace(), CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        sqsClient.Verify(
            s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task ValidateConnectionAsync_NonTransientAuthError_IsNotRetried()
    {
        var sqsClient = new Mock<IAmazonSQS>();
        var authError = new AmazonSQSException("denied")
        {
            ErrorCode = "InvalidClientTokenId",
            StatusCode = System.Net.HttpStatusCode.Forbidden,
        };
        sqsClient.Setup(s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(authError);

        var factory = new Mock<IAwsClientFactory>();
        factory.Setup(f => f.GetSqsClient(It.IsAny<Namespace>())).Returns(sqsClient.Object);
        var provider = BuildProvider(factory: factory.Object);

        var result = await provider.ValidateConnectionAsync(BuildNamespace(), CancellationToken.None);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("AWS.SQS.AuthFailed");
        sqsClient.Verify(
            s => s.ListQueuesAsync(It.IsAny<ListQueuesRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
