using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class CrossCloudTraceControllerTests
{
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly Mock<IServiceBusClientCache> _clientCacheMock = new();
    private readonly Mock<IConnectionStringProtector> _connectionStringProtectorMock = new();
    private readonly Mock<ILogger<CrossCloudTraceController>> _loggerMock = new();
    private readonly List<ICloudMessagingProvider> _cloudProviders = new();
    private readonly CrossCloudTraceController _controller;

    public CrossCloudTraceControllerTests()
    {
        _controller = new CrossCloudTraceController(
            _namespaceRepositoryMock.Object,
            _clientCacheMock.Object,
            _connectionStringProtectorMock.Object,
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
}
