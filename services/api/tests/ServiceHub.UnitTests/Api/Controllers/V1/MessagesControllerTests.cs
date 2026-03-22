using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class MessagesControllerTests
{
    private readonly Mock<IMessageSender> _messageSender;
    private readonly Mock<IMessageReceiver> _messageReceiver;
    private readonly Mock<INamespaceRepository> _namespaceRepository;
    private readonly Mock<ILogger<MessagesController>> _logger;
    private readonly MessagesController _controller;

    public MessagesControllerTests()
    {
        _messageSender = new Mock<IMessageSender>();
        _messageReceiver = new Mock<IMessageReceiver>();
        _namespaceRepository = new Mock<INamespaceRepository>();
        _logger = new Mock<ILogger<MessagesController>>();

        _controller = new MessagesController(
            _messageSender.Object,
            _messageReceiver.Object,
            _namespaceRepository.Object,
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

    #region Constructor Tests

    [Fact]
    public void Constructor_NullMessageSender_ShouldThrow()
    {
        var act = () => new MessagesController(null!, _messageReceiver.Object, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullMessageReceiver_ShouldThrow()
    {
        var act = () => new MessagesController(_messageSender.Object, null!, _namespaceRepository.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new MessagesController(_messageSender.Object, _messageReceiver.Object, _namespaceRepository.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region PeekQueueMessages Tests

    [Fact]
    public async Task PeekQueueMessages_Success_ShouldReturnOk()
    {
        var nsId = Guid.NewGuid();
        var messages = new List<Message>
        {
            new() { MessageId = "msg-1", SequenceNumber = 1, EnqueuedTime = DateTimeOffset.UtcNow },
            new() { MessageId = "msg-2", SequenceNumber = 2, EnqueuedTime = DateTimeOffset.UtcNow }
        };

        _messageReceiver.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));

        var result = await _controller.PeekQueueMessages(nsId, "my-queue");

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var responses = okResult.Value.Should().BeAssignableTo<List<MessageResponse>>().Subject;
        responses.Should().HaveCount(2);
    }

    [Fact]
    public async Task PeekQueueMessages_Failure_ShouldReturnError()
    {
        var nsId = Guid.NewGuid();
        _messageReceiver.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Failure(Error.ExternalService("SB_ERR", "Service Bus error")));

        var result = await _controller.PeekQueueMessages(nsId, "my-queue");

        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task PeekQueueMessages_ShouldClampMaxMessages()
    {
        var nsId = Guid.NewGuid();
        _messageReceiver.Setup(r => r.PeekMessagesAsync(
            It.Is<GetMessagesRequest>(req => req.MaxMessages <= 100 && req.MaxMessages >= 1),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new List<Message>()));

        await _controller.PeekQueueMessages(nsId, "my-queue", maxMessages: 999);

        _messageReceiver.Verify(
            r => r.PeekMessagesAsync(
                It.Is<GetMessagesRequest>(req => req.MaxMessages == 100),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region PeekSubscriptionMessages Tests

    [Fact]
    public async Task PeekSubscriptionMessages_Success_ShouldReturnOk()
    {
        var nsId = Guid.NewGuid();
        var messages = new List<Message>
        {
            new() { MessageId = "msg-1", SequenceNumber = 1, EnqueuedTime = DateTimeOffset.UtcNow }
        };

        _messageReceiver.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));

        var result = await _controller.PeekSubscriptionMessages(nsId, "my-topic", "my-sub");

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    }

    #endregion

    #region PeekQueueDeadLetterMessages Tests

    [Fact]
    public async Task PeekQueueDeadLetterMessages_Success_ShouldReturnOk()
    {
        var nsId = Guid.NewGuid();
        var messages = new List<Message>
        {
            new() { MessageId = "dlq-1", SequenceNumber = 1, EnqueuedTime = DateTimeOffset.UtcNow, IsFromDeadLetter = true }
        };

        _messageReceiver.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));

        var result = await _controller.PeekQueueDeadLetterMessages(nsId, "my-queue");

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    }

    #endregion

    #region PeekSubscriptionDeadLetterMessages Tests

    [Fact]
    public async Task PeekSubscriptionDeadLetterMessages_Success_ShouldReturnOk()
    {
        var nsId = Guid.NewGuid();
        var messages = new List<Message>
        {
            new() { MessageId = "dlq-1", SequenceNumber = 1, EnqueuedTime = DateTimeOffset.UtcNow, IsFromDeadLetter = true }
        };

        _messageReceiver.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));

        var result = await _controller.PeekSubscriptionDeadLetterMessages(nsId, "my-topic", "my-sub");

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    }

    #endregion

    #region ReplayMessage Tests

    [Fact]
    public async Task ReplayMessage_Success_ShouldReturnAccepted()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _messageReceiver.Setup(r => r.ReplayMessageAsync(ns.Id, "my-queue", null, 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.ReplayMessage(ns.Id, 42, "my-queue");

        result.Should().BeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task ReplayMessage_NamespaceNotFound_ShouldReturnError()
    {
        var id = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.ReplayMessage(id, 42, "my-queue");

        result.Should().NotBeOfType<AcceptedResult>();
    }

    [Fact]
    public async Task ReplayMessage_WithSubscription_ShouldPassSubscriptionName()
    {
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _messageReceiver.Setup(r => r.ReplayMessageAsync(ns.Id, "my-topic", "my-sub", 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.ReplayMessage(ns.Id, 42, "my-topic", "my-sub");

        result.Should().BeOfType<AcceptedResult>();
    }

    #endregion
}
