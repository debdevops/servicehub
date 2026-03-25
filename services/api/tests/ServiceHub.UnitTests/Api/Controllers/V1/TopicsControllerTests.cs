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
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class TopicsControllerTests
{
    private readonly Mock<INamespaceRepository> _namespaceRepository;
    private readonly Mock<IServiceBusClientCache> _clientCache;
    private readonly Mock<IConnectionStringProtector> _connectionStringProtector;
    private readonly Mock<IMessageSender> _messageSender;
    private readonly Mock<IMessageReceiver> _messageReceiver;
    private readonly Mock<ILogger<TopicsController>> _logger;
    private readonly TopicsController _controller;

    public TopicsControllerTests()
    {
        _namespaceRepository = new Mock<INamespaceRepository>();
        _clientCache = new Mock<IServiceBusClientCache>();
        _connectionStringProtector = new Mock<IConnectionStringProtector>();
        _messageSender = new Mock<IMessageSender>();
        _messageReceiver = new Mock<IMessageReceiver>();
        _logger = new Mock<ILogger<TopicsController>>();

        _controller = new TopicsController(
            _namespaceRepository.Object,
            _clientCache.Object,
            _connectionStringProtector.Object,
            _messageSender.Object,
            _messageReceiver.Object,
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    private static Namespace CreateTestNamespace()
    {
        var result = Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS");
        return result.Value;
    }

    private static TopicRuntimePropertiesDto CreateTestTopic(string name = "test-topic")
    {
        return new TopicRuntimePropertiesDto(
            Name: name,
            SubscriptionCount: 3,
            SizeInBytes: 2048,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-7),
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresDuplicateDetection: false,
            EnablePartitioning: false,
            EnableBatchedOperations: true,
            SupportOrdering: false,
            MaxSizeInMegabytes: 1024,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            AutoDeleteOnIdle: TimeSpan.MaxValue,
            DuplicateDetectionHistoryTimeWindow: TimeSpan.FromMinutes(10));
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullRepository_ShouldThrow()
    {
        var act = () => new TopicsController(
            null!, _clientCache.Object, _connectionStringProtector.Object,
            _messageSender.Object, _messageReceiver.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new TopicsController(
            _namespaceRepository.Object, _clientCache.Object, _connectionStringProtector.Object,
            _messageSender.Object, _messageReceiver.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region GetAll Tests

    [Fact]
    public async Task GetAll_Success_ShouldReturnOkWithTopics()
    {
        var ns = CreateTestNamespace();
        var topics = new List<TopicRuntimePropertiesDto> { CreateTestTopic("topic1"), CreateTestTopic("topic2") };

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success("conn-string"));

        var wrapper = new Mock<IServiceBusClientWrapper>();
        wrapper.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(topics));

        _clientCache.Setup(c => c.GetOrCreate(ns.Id, It.IsAny<string>()))
            .Returns(wrapper.Object);

        var result = await _controller.GetAll(ns.Id);

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedTopics = okResult.Value.Should().BeAssignableTo<IReadOnlyList<TopicRuntimePropertiesDto>>().Subject;
        returnedTopics.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAll_NamespaceNotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.GetAll(id);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetAll_NoConnectionString_ShouldReturnBadRequest()
    {
        var nsResult = Namespace.CreateWithManagedIdentity("test-mi", ConnectionAuthType.ManagedIdentity);
        var ns = nsResult.Value;

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var result = await _controller.GetAll(ns.Id);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    #endregion

    #region GetByName Tests

    [Fact]
    public async Task GetByName_Success_ShouldReturnOkWithTopic()
    {
        var ns = CreateTestNamespace();
        var topic = CreateTestTopic("my-topic");

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success("conn-string"));

        var wrapper = new Mock<IServiceBusClientWrapper>();
        wrapper.Setup(w => w.GetTopicAsync("my-topic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<TopicRuntimePropertiesDto>.Success(topic));

        _clientCache.Setup(c => c.GetOrCreate(ns.Id, It.IsAny<string>()))
            .Returns(wrapper.Object);

        var result = await _controller.GetByName(ns.Id, "my-topic");

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var returnedTopic = okResult.Value.Should().BeOfType<TopicRuntimePropertiesDto>().Subject;
        returnedTopic.Name.Should().Be("my-topic");
    }

    [Fact]
    public async Task GetByName_NamespaceNotFound_ShouldReturnNotFound()
    {
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.GetByName(id, "my-topic");

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    #endregion

    #region GetSubscriptionMessages Tests

    [Fact]
    public async Task GetSubscriptionMessages_Success_ReturnsOkWithMessages()
    {
        var ns = CreateTestNamespace();
        var nsId = ns.Id;

        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var messages = new List<Message>
        {
            new() { MessageId = "msg-1", SequenceNumber = 1, Body = "hello", EnqueuedTime = DateTimeOffset.UtcNow, DeliveryCount = 1 }
        };

        _messageReceiver.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success("Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Test;SharedAccessKey=abc="));

        var wrapperMock = new Mock<IServiceBusClientWrapper>();
        var subDto = new SubscriptionRuntimePropertiesDto(
            "test-sub", "test-topic", 10, 5, 0, 0, "Active",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            false, true, true, true, 10,
            TimeSpan.FromDays(14), TimeSpan.FromMinutes(1), TimeSpan.MaxValue, null, null);
        wrapperMock.Setup(w => w.GetSubscriptionsAsync("test-topic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SubscriptionRuntimePropertiesDto>>.Success(new[] { subDto }));
        _clientCache.Setup(c => c.GetOrCreate(nsId, It.IsAny<string>()))
            .Returns(wrapperMock.Object);

        var result = await _controller.GetSubscriptionMessages(nsId, "test-topic", "test-sub");

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetSubscriptionMessages_DeadLetter_ReturnsOk()
    {
        var ns = CreateTestNamespace();
        var nsId = ns.Id;

        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var messages = new List<Message>();
        _messageReceiver.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success("Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=Test;SharedAccessKey=abc="));

        var wrapperMock = new Mock<IServiceBusClientWrapper>();
        wrapperMock.Setup(w => w.GetSubscriptionsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SubscriptionRuntimePropertiesDto>>.Success(
                Array.Empty<SubscriptionRuntimePropertiesDto>()));
        _clientCache.Setup(c => c.GetOrCreate(nsId, It.IsAny<string>()))
            .Returns(wrapperMock.Object);

        var result = await _controller.GetSubscriptionMessages(nsId, "test-topic", "test-sub", queueType: "deadletter");

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetSubscriptionMessages_PeekFails_ReturnsError()
    {
        var ns = CreateTestNamespace();
        var nsId = ns.Id;

        _messageReceiver.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Failure(Error.ExternalService("err", "timeout")));

        var result = await _controller.GetSubscriptionMessages(nsId, "test-topic", "test-sub");

        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetSubscriptionMessages_NamespaceNotFound_StillReturnsPeekedCount()
    {
        var nsId = Guid.NewGuid();

        var messages = new List<Message>
        {
            new() { MessageId = "msg-1", SequenceNumber = 1, Body = "hello", EnqueuedTime = DateTimeOffset.UtcNow, DeliveryCount = 1 }
        };

        _messageReceiver.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));

        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("ns", "not found")));

        var result = await _controller.GetSubscriptionMessages(nsId, "test-topic", "test-sub");

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    #endregion

    #region DeadLetterSubscriptionMessages Tests

    [Fact]
    public async Task DeadLetterSubscriptionMessages_Success_ReturnsOk()
    {
        var ns = CreateTestNamespace();
        var nsId = ns.Id;

        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _messageReceiver.Setup(r => r.DeadLetterMessagesAsync(It.IsAny<DeadLetterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(3));

        var result = await _controller.DeadLetterSubscriptionMessages(nsId, "test-topic", "test-sub", messageCount: 3);

        result.Result.Should().BeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task DeadLetterSubscriptionMessages_NamespaceNotFound_ReturnsError()
    {
        var nsId = Guid.NewGuid();

        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("ns", "not found")));

        var result = await _controller.DeadLetterSubscriptionMessages(nsId, "test-topic", "test-sub");

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task DeadLetterSubscriptionMessages_NoSendPermission_Returns403()
    {
        // Create namespace with listen-only key (no send permission)
        var ns = Namespace.Create(
            "test-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=ListenPolicy;SharedAccessKey=testkey123=",
            "Test NS").Value;

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var result = await _controller.DeadLetterSubscriptionMessages(ns.Id, "test-topic", "test-sub");

        var objectResult = result.Result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task DeadLetterSubscriptionMessages_ReceiverFails_ReturnsError()
    {
        var ns = CreateTestNamespace();
        var nsId = ns.Id;

        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _messageReceiver.Setup(r => r.DeadLetterMessagesAsync(It.IsAny<DeadLetterRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Failure(Error.ExternalService("err", "Service Bus error")));

        var result = await _controller.DeadLetterSubscriptionMessages(nsId, "test-topic", "test-sub");

        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    #endregion

    #region Production Safety Guard Tests

    [Fact]
    public async Task SendMessage_ProductionNamespace_Returns403()
    {
        var ns = Namespace.Create(
            "prod-namespace",
            "Endpoint=sb://prod.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Prod NS",
            environment: EnvironmentType.Prod).Value;

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var request = new SendMessageRequest(Body: "test message");
        var result = await _controller.SendMessage(ns.Id, "test-topic", request);

        var objectResult = result as ObjectResult;
        objectResult.Should().NotBeNull();
        objectResult!.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task SendMessage_DevNamespace_Allowed()
    {
        var ns = Namespace.Create(
            "dev-namespace",
            "Endpoint=sb://dev.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Dev NS",
            environment: EnvironmentType.Dev).Value;

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));
        _messageSender.Setup(s => s.SendAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var request = new SendMessageRequest(Body: "test message");
        var result = await _controller.SendMessage(ns.Id, "test-topic", request);

        result.Should().BeOfType<AcceptedResult>();
    }

    #endregion
}
