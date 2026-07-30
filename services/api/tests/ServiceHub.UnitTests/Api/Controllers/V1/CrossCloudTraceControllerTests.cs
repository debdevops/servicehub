using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Api.Services;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class CrossCloudTraceControllerTests : IDisposable
{
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly Mock<IServiceBusClientCache> _clientCacheMock = new();
    private readonly Mock<IConnectionStringProtector> _connectionStringProtectorMock = new();
    private readonly Mock<ILogger<CrossCloudTraceController>> _loggerMock = new();
    private readonly List<ICloudMessagingProvider> _cloudProviders = new();
    private readonly DlqDbContext _dlqContext;
    private readonly CrossCloudTraceController _controller;

    public CrossCloudTraceControllerTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dlqContext = new DlqDbContext(options);
        _dlqContext.Database.OpenConnection();
        _dlqContext.Database.EnsureCreated();

        // Use a real AzureTraceSearcher backed by the same cache/protector mocks so the
        // extracted Azure search algorithm remains exercised end-to-end via these tests.
        var azureTraceSearcher = new AzureTraceSearcher(
            _clientCacheMock.Object,
            _connectionStringProtectorMock.Object,
            NullLogger<AzureTraceSearcher>.Instance);

        _controller = new CrossCloudTraceController(
            _namespaceRepositoryMock.Object,
            azureTraceSearcher,
            _dlqContext,
            _loggerMock.Object,
            _cloudProviders)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Items = { { "OwnerId", TestConstants.TestOwnerId } }
                }
            }
        };
    }

    public void Dispose()
    {
        _dlqContext.Database.CloseConnection();
        _dlqContext.Dispose();
    }

    private DlqMessage MakeDlqRecord(
        string messageId,
        string correlationId,
        CloudProviderType provider = CloudProviderType.Azure,
        DlqMessageStatus status = DlqMessageStatus.Replayed,
        string ownerId = TestConstants.TestOwnerId,
        DateTimeOffset? detectedAt = null) =>
        new()
        {
            MessageId = messageId,
            SequenceNumber = 1,
            BodyHash = "abc",
            NamespaceId = Guid.NewGuid(),
            OwnerId = ownerId,
            CloudProvider = provider,
            CorrelationId = correlationId,
            EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = detectedAt ?? DateTimeOffset.UtcNow,
            DetectedAtUtc = detectedAt ?? DateTimeOffset.UtcNow,
            DeadLetterReason = "MaxDeliveryCountExceeded",
            BodyPreview = "preview",
            MessageSize = 128,
            Status = status,
        };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task TraceMessage_WithInvalidTraceId_ReturnsBadRequest(string? traceId)
    {
        // Act
        var result = await _controller.TraceMessage(traceId);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task TraceMessage_NamespaceRepositoryFailure_ReturnsError()
    {
        // Arrange
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(Error.Internal("Repository.Error", "Database is down.")));

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert
        result.Result.Should().BeOfType<ObjectResult>();
        var objectResult = (ObjectResult)result.Result!;
        objectResult.StatusCode.Should().Be(500);
    }

    [Fact]
    public async Task TraceMessage_NoNamespaces_ReturnsEmptyResponse()
    {
        // Arrange
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace>()));

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var response = (CrossCloudTraceResponse)okResult.Value!;
        response.TraceId.Should().Be("trace-123");
        response.Hops.Should().BeEmpty();
        response.NamespaceSummaries.Should().BeEmpty();
    }

    [Fact]
    public async Task TraceMessage_AzureNamespace_WithNullConnectionString_SkipsNamespace()
    {
        // Arrange
        var ns = Namespace.CreateWithManagedIdentity("my-ns.servicebus.windows.net", ConnectionAuthType.ManagedIdentity).Value;
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var response = (CrossCloudTraceResponse)okResult.Value!;
        response.NamespaceSummaries.Should().HaveCount(1);
        response.NamespaceSummaries[0].WasSearched.Should().BeFalse();
        response.NamespaceSummaries[0].SkipReason.Should().Be("No connection string configured");
    }

    [Fact]
    public async Task TraceMessage_AzureNamespace_ConnectionStringDecryptionFailure_SkipsNamespace()
    {
        // Arrange
        var ns = Namespace.Create("my-ns.servicebus.windows.net", "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123=").Value;
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        _connectionStringProtectorMock
            .Setup(x => x.Unprotect(ns.ConnectionString!))
            .Returns(Result<string>.Failure(Error.Validation("Decrypt.Error", "Failed to decrypt.")));

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var response = (CrossCloudTraceResponse)okResult.Value!;
        response.NamespaceSummaries.Should().HaveCount(1);
        response.NamespaceSummaries[0].WasSearched.Should().BeFalse();
        response.NamespaceSummaries[0].SkipReason.Should().Be("Connection string decryption failed");
    }

    [Fact]
    public async Task TraceMessage_AzureNamespace_SuccessfulTrace_FindsHops()
    {
        // Arrange
        var ns = Namespace.Create("my-ns.servicebus.windows.net", "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123=").Value;
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        _connectionStringProtectorMock
            .Setup(x => x.Unprotect(ns.ConnectionString!))
            .Returns(Result<string>.Success("decrypted-conn-string"));

        var clientWrapperMock = new Mock<IServiceBusClientWrapper>();
        _clientCacheMock
            .Setup(x => x.GetOrCreate(ns.Id, "decrypted-conn-string"))
            .Returns(clientWrapperMock.Object);

        // Queues: we return one queue with DeadLetterMessageCount = 1
        var queues = new List<QueueRuntimePropertiesDto>
        {
            new QueueRuntimePropertiesDto(
                Name: "q1",
                ActiveMessageCount: 1,
                DeadLetterMessageCount: 1,
                ScheduledMessageCount: 0,
                TransferMessageCount: 0,
                TransferDeadLetterMessageCount: 0,
                SizeInBytes: 100,
                Status: "Active",
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                AccessedAt: DateTimeOffset.UtcNow,
                RequiresSession: false,
                RequiresDuplicateDetection: false,
                EnablePartitioning: false,
                EnableBatchedOperations: true,
                MaxSizeInMegabytes: 1024,
                MaxDeliveryCount: 10,
                DefaultMessageTimeToLive: TimeSpan.FromDays(1),
                LockDuration: TimeSpan.FromSeconds(30),
                AutoDeleteOnIdle: TimeSpan.MaxValue
            )
        };
        clientWrapperMock
            .Setup(x => x.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(queues));

        // Topics: we return one topic
        var topics = new List<TopicRuntimePropertiesDto>
        {
            new TopicRuntimePropertiesDto(
                Name: "t1",
                SubscriptionCount: 1,
                SizeInBytes: 100,
                Status: "Active",
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                AccessedAt: DateTimeOffset.UtcNow,
                RequiresDuplicateDetection: false,
                EnablePartitioning: false,
                EnableBatchedOperations: true,
                SupportOrdering: true,
                MaxSizeInMegabytes: 1024,
                DefaultMessageTimeToLive: TimeSpan.FromDays(1),
                AutoDeleteOnIdle: TimeSpan.MaxValue,
                DuplicateDetectionHistoryTimeWindow: TimeSpan.Zero
            )
        };
        clientWrapperMock
            .Setup(x => x.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(topics));

        // Subscriptions for t1
        var subscriptions = new List<SubscriptionRuntimePropertiesDto>
        {
            new SubscriptionRuntimePropertiesDto(
                Name: "s1",
                TopicName: "t1",
                ActiveMessageCount: 1,
                DeadLetterMessageCount: 1,
                TransferMessageCount: 0,
                TransferDeadLetterMessageCount: 0,
                Status: "Active",
                CreatedAt: DateTimeOffset.UtcNow,
                UpdatedAt: DateTimeOffset.UtcNow,
                AccessedAt: DateTimeOffset.UtcNow,
                RequiresSession: false,
                EnableBatchedOperations: true,
                EnableDeadLetteringOnMessageExpiration: false,
                EnableDeadLetteringOnFilterEvaluationExceptions: false,
                MaxDeliveryCount: 10,
                DefaultMessageTimeToLive: TimeSpan.FromDays(1),
                LockDuration: TimeSpan.FromSeconds(30),
                AutoDeleteOnIdle: TimeSpan.MaxValue,
                ForwardTo: null,
                ForwardDeadLetteredMessagesTo: null
            )
        };
        clientWrapperMock
            .Setup(x => x.GetSubscriptionsAsync("t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SubscriptionRuntimePropertiesDto>>.Success(subscriptions));

        // Mock PeekMessages for:
        // 1. Live messages on q1 -> 1 matching
        var liveQ1Message = new Message
        {
            MessageId = "m-live-q1",
            SequenceNumber = 10,
            CorrelationId = "trace-123",
            State = MessageState.Active,
            EnqueuedTime = DateTimeOffset.UtcNow.AddMinutes(-5),
            SizeInBytes = 200
        };
        clientWrapperMock
            .Setup(x => x.PeekMessagesAsync(
                It.Is<GetMessagesRequest>(r => r.EntityName == "q1" && r.SubscriptionName == null && r.FromDeadLetter == false),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new List<Message> { liveQ1Message }));

        // 2. Dead letter messages on q1 -> 1 matching
        var dlqQ1Message = new Message
        {
            MessageId = "m-dlq-q1",
            SequenceNumber = 11,
            CorrelationId = "trace-123",
            State = MessageState.DeadLettered,
            EnqueuedTime = DateTimeOffset.UtcNow.AddMinutes(-4),
            SizeInBytes = 200,
            DeadLetterReason = "Some error"
        };
        clientWrapperMock
            .Setup(x => x.PeekMessagesAsync(
                It.Is<GetMessagesRequest>(r => r.EntityName == "q1" && r.SubscriptionName == null && r.FromDeadLetter == true),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new List<Message> { dlqQ1Message }));

        // 3. Live messages on t1/subscriptions/s1 -> 1 matching
        var liveSubMessage = new Message
        {
            MessageId = "m-live-sub",
            SequenceNumber = 20,
            CorrelationId = "trace-123",
            State = MessageState.Active,
            EnqueuedTime = DateTimeOffset.UtcNow.AddMinutes(-3),
            SizeInBytes = 200
        };
        clientWrapperMock
            .Setup(x => x.PeekMessagesAsync(
                It.Is<GetMessagesRequest>(r => r.EntityName == "t1" && r.SubscriptionName == "s1" && r.FromDeadLetter == false),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new List<Message> { liveSubMessage }));

        // 4. Dead letter messages on t1/subscriptions/s1 -> 1 matching
        var dlqSubMessage = new Message
        {
            MessageId = "m-dlq-sub",
            SequenceNumber = 21,
            CorrelationId = "trace-123",
            State = MessageState.DeadLettered,
            EnqueuedTime = DateTimeOffset.UtcNow.AddMinutes(-2),
            SizeInBytes = 200,
            DeadLetterReason = "Some other error"
        };
        clientWrapperMock
            .Setup(x => x.PeekMessagesAsync(
                It.Is<GetMessagesRequest>(r => r.EntityName == "t1" && r.SubscriptionName == "s1" && r.FromDeadLetter == true),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new List<Message> { dlqSubMessage }));

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var response = (CrossCloudTraceResponse)okResult.Value!;
        response.Hops.Should().HaveCount(4);
        response.NamespaceSummaries.Should().HaveCount(1);
        response.NamespaceSummaries[0].WasSearched.Should().BeTrue();
        response.NamespaceSummaries[0].HopsFound.Should().Be(4);
    }

    [Fact]
    public async Task TraceMessage_NonAzureNamespace_NoMatchingProvider_SkipsNamespace()
    {
        // Arrange
        var ns = Namespace.Create("aws-queue", "https://sqs.us-east-1.amazonaws.com/123456789012/my-queue", provider: CloudProviderType.Aws).Value;
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var response = (CrossCloudTraceResponse)okResult.Value!;
        response.NamespaceSummaries.Should().HaveCount(1);
        response.NamespaceSummaries[0].WasSearched.Should().BeFalse();
        response.NamespaceSummaries[0].SkipReason.Should().Be("AWS provider is not enabled on this server.");
    }

    [Fact]
    public async Task TraceMessage_NonAzureNamespace_MatchingProvider_SuccessfulTrace_FindsHops()
    {
        // Arrange
        var ns = Namespace.Create("aws-queue", "https://sqs.us-east-1.amazonaws.com/123456789012/my-queue", provider: CloudProviderType.Aws).Value;
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        var providerMock = new Mock<ICloudMessagingProvider>();
        providerMock.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        providerMock.SetupGet(p => p.Capabilities).Returns(ProviderCapabilities.Aws);
        _cloudProviders.Add(providerMock.Object);

        // List entities
        var entities = new List<CloudEntity>
        {
            new CloudEntity { Name = "aws-queue-1", EntityType = "Queue", Provider = CloudProviderType.Aws }
        };
        providerMock
            .Setup(x => x.ListEntitiesAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Success(entities));

        // Mock receiver and PeekMessages
        var receiverMock = new Mock<IMessageReceiver>();
        providerMock.Setup(x => x.GetMessageReceiver()).Returns(receiverMock.Object);

        // One message with TraceId in application properties
        var appProps = new Dictionary<string, object>
        {
            { "traceId", "trace-123" }
        };
        var msg = new Message
        {
            MessageId = "m-aws",
            SequenceNumber = 100,
            State = MessageState.Active,
            EnqueuedTime = DateTimeOffset.UtcNow,
            SizeInBytes = 200,
            ApplicationProperties = appProps
        };
        receiverMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new List<Message> { msg }));

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var response = (CrossCloudTraceResponse)okResult.Value!;
        response.Hops.Should().HaveCount(1);
        response.NamespaceSummaries.Should().HaveCount(1);
        response.NamespaceSummaries[0].WasSearched.Should().BeTrue();
        response.NamespaceSummaries[0].HopsFound.Should().Be(1);
    }

    [Fact]
    public async Task TraceMessage_NonAzureNamespace_MatchOnlyInDeadLetterQueue_StillFindsHop()
    {
        // Arrange — GCP: SupportsMessageCounts is false, so the dead-letter search must run
        // unconditionally rather than being gated on entity.DeadLetterCount (which GCP never
        // populates). This is the parity fix: Azure's IAzureTraceSearcher always checks the
        // DLQ too; before the fix, this controller's non-Azure path only peeked active messages.
        var ns = Namespace.Create("gcp-topic", "{\"type\":\"service_account\"}", provider: CloudProviderType.Gcp).Value;
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        var providerMock = new Mock<ICloudMessagingProvider>();
        providerMock.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Gcp);
        providerMock.SetupGet(p => p.Capabilities).Returns(ProviderCapabilities.Gcp);
        _cloudProviders.Add(providerMock.Object);

        var entities = new List<CloudEntity>
        {
            // DeadLetterCount defaults to 0 — exactly what GCP's real provider always reports.
            new CloudEntity { Name = "gcp-sub", EntityType = "Subscription", Provider = CloudProviderType.Gcp }
        };
        providerMock
            .Setup(x => x.ListEntitiesAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Success(entities));

        var receiverMock = new Mock<IMessageReceiver>();
        providerMock.Setup(x => x.GetMessageReceiver()).Returns(receiverMock.Object);

        // Active peek: no match.
        receiverMock
            .Setup(x => x.PeekMessagesAsync(
                It.Is<GetMessagesRequest>(r => !r.FromDeadLetter), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new List<Message>()));

        // Dead-letter peek: the traced message.
        var dlqMsg = new Message
        {
            MessageId = "m-gcp-dlq",
            SequenceNumber = 200,
            State = MessageState.DeadLettered,
            EnqueuedTime = DateTimeOffset.UtcNow,
            SizeInBytes = 150,
            CorrelationId = "trace-456"
        };
        receiverMock
            .Setup(x => x.PeekMessagesAsync(
                It.Is<GetMessagesRequest>(r => r.FromDeadLetter), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new List<Message> { dlqMsg }));

        // Act
        var result = await _controller.TraceMessage("trace-456");

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var okResult = (OkObjectResult)result.Result!;
        var response = (CrossCloudTraceResponse)okResult.Value!;
        response.Hops.Should().ContainSingle(h => h.MessageId == "m-gcp-dlq");
        response.Hops[0].EntityPath.Should().Be("gcp-sub/$DeadLetterQueue");
        response.NamespaceSummaries[0].HopsFound.Should().Be(1);
    }

    // ── Historical DLQ merge ──────────────────────────────────────────────

    [Fact]
    public async Task TraceMessage_HistoricalDlqRecord_AppearsAsHistoryHop_WithProviderTag()
    {
        // Arrange — no live namespaces; only a historical GCP DLQ record matches.
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace>()));

        _dlqContext.DlqMessages.Add(MakeDlqRecord(
            "m-hist-gcp", "trace-123", CloudProviderType.Gcp, DlqMessageStatus.Replayed));
        await _dlqContext.SaveChangesAsync();

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var response = (CrossCloudTraceResponse)((OkObjectResult)result.Result!).Value!;
        response.Hops.Should().HaveCount(1);
        response.Hops[0].Source.Should().Be("History");
        response.Hops[0].CloudProvider.Should().Be("gcp");
        response.Hops[0].State.Should().Be("Replayed");
        response.Hops[0].MessageId.Should().Be("m-hist-gcp");
        response.CloudProviders.Should().Contain("gcp");
    }

    [Fact]
    public async Task TraceMessage_LiveHopWithSameMessageId_SuppressesHistoryDuplicate()
    {
        // Arrange — AWS live search finds "m-aws"; a stale DLQ record for the same
        // MessageId must be superseded by the live hop.
        var ns = Namespace.Create("aws-queue", "https://sqs.us-east-1.amazonaws.com/123456789012/my-queue", provider: CloudProviderType.Aws).Value;
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        var providerMock = new Mock<ICloudMessagingProvider>();
        providerMock.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        providerMock.SetupGet(p => p.Capabilities).Returns(ProviderCapabilities.Aws);
        _cloudProviders.Add(providerMock.Object);

        providerMock
            .Setup(x => x.ListEntitiesAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Success(new List<CloudEntity>
            {
                new CloudEntity { Name = "aws-queue-1", EntityType = "Queue", Provider = CloudProviderType.Aws }
            }));

        var receiverMock = new Mock<IMessageReceiver>();
        providerMock.Setup(x => x.GetMessageReceiver()).Returns(receiverMock.Object);
        receiverMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new List<Message>
            {
                new Message
                {
                    MessageId = "m-aws",
                    SequenceNumber = 100,
                    CorrelationId = "trace-123",
                    State = MessageState.Active,
                    EnqueuedTime = DateTimeOffset.UtcNow,
                    SizeInBytes = 200
                }
            }));

        _dlqContext.DlqMessages.Add(MakeDlqRecord(
            "m-aws", "trace-123", CloudProviderType.Aws, DlqMessageStatus.Replayed));
        await _dlqContext.SaveChangesAsync();

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert — one hop only, and it is the live one.
        var response = (CrossCloudTraceResponse)((OkObjectResult)result.Result!).Value!;
        response.Hops.Should().HaveCount(1);
        response.Hops[0].Source.Should().Be("Live");
    }

    [Fact]
    public async Task TraceMessage_HistoryAndLive_MergedChronologically_WithHopIndex()
    {
        // Arrange — a history record older than the live hop must sort first.
        var ns = Namespace.Create("aws-queue", "https://sqs.us-east-1.amazonaws.com/123456789012/my-queue", provider: CloudProviderType.Aws).Value;
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        var providerMock = new Mock<ICloudMessagingProvider>();
        providerMock.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Aws);
        providerMock.SetupGet(p => p.Capabilities).Returns(ProviderCapabilities.Aws);
        _cloudProviders.Add(providerMock.Object);

        providerMock
            .Setup(x => x.ListEntitiesAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Success(new List<CloudEntity>
            {
                new CloudEntity { Name = "aws-queue-1", EntityType = "Queue", Provider = CloudProviderType.Aws }
            }));

        var receiverMock = new Mock<IMessageReceiver>();
        providerMock.Setup(x => x.GetMessageReceiver()).Returns(receiverMock.Object);
        receiverMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new List<Message>
            {
                new Message
                {
                    MessageId = "m-aws-live",
                    SequenceNumber = 100,
                    CorrelationId = "trace-123",
                    State = MessageState.Active,
                    EnqueuedTime = DateTimeOffset.UtcNow,
                    SizeInBytes = 200
                }
            }));

        _dlqContext.DlqMessages.Add(MakeDlqRecord(
            "m-azure-hist", "trace-123", CloudProviderType.Azure, DlqMessageStatus.Replayed,
            detectedAt: DateTimeOffset.UtcNow.AddMinutes(-10)));
        await _dlqContext.SaveChangesAsync();

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert
        var response = (CrossCloudTraceResponse)((OkObjectResult)result.Result!).Value!;
        response.Hops.Should().HaveCount(2);
        response.Hops[0].Source.Should().Be("History");
        response.Hops[0].HopIndex.Should().Be(0);
        response.Hops[1].Source.Should().Be("Live");
        response.Hops[1].HopIndex.Should().Be(1);
        response.IsMultiCloud.Should().BeTrue(); // azure (history) + aws (live)
    }

    [Fact]
    public async Task TraceMessage_HistoricalRecordOfDifferentOwner_NotIncluded()
    {
        // Arrange — tenant isolation: another owner's DLQ record must never leak.
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace>()));

        _dlqContext.DlqMessages.Add(MakeDlqRecord(
            "m-other-owner", "trace-123", ownerId: "entra:someone-else"));
        await _dlqContext.SaveChangesAsync();

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert
        var response = (CrossCloudTraceResponse)((OkObjectResult)result.Result!).Value!;
        response.Hops.Should().BeEmpty();
    }

    [Fact]
    public async Task TraceMessage_HistoryQueryFails_StillReturnsLiveResults()
    {
        // Arrange — closing the SQLite connection makes the history query throw;
        // the trace must degrade to live-only results, not fail.
        _namespaceRepositoryMock
            .Setup(x => x.GetByOwnerAsync(TestConstants.TestOwnerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace>()));

        _dlqContext.Database.CloseConnection();

        // Act
        var result = await _controller.TraceMessage("trace-123");

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        var response = (CrossCloudTraceResponse)((OkObjectResult)result.Result!).Value!;
        response.Hops.Should().BeEmpty();
    }
}
