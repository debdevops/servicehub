using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Api.Security;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class MessagesControllerTests
{
    private readonly Mock<IMessageOperationsService> _messageOperationsService;
    private readonly Mock<INamespaceRepository> _namespaceRepository;
    private readonly Mock<ICloudMessagingProvider> _cloudProvider;
    private readonly CloudProviderRouter _providerRouter;
    private readonly Mock<ILiveTailSessionFactory> _liveTailSessionFactory;
    private readonly Mock<ILiveTailConnectionLimiter> _liveTailConnectionLimiter;
    private readonly Mock<ILogger<MessagesController>> _logger;
    private readonly MessagesController _controller;

    public MessagesControllerTests()
    {
        _messageOperationsService = new Mock<IMessageOperationsService>();
        _namespaceRepository = new Mock<INamespaceRepository>();
        _cloudProvider = new Mock<ICloudMessagingProvider>();
        _cloudProvider.SetupGet(p => p.ProviderType).Returns(CloudProviderType.Azure);
        _cloudProvider.SetupGet(p => p.Capabilities).Returns(ProviderCapabilities.Azure);
        _providerRouter = new CloudProviderRouter([_cloudProvider.Object]);
        _liveTailSessionFactory = new Mock<ILiveTailSessionFactory>();
        _liveTailConnectionLimiter = new Mock<ILiveTailConnectionLimiter>();
        _logger = new Mock<ILogger<MessagesController>>();

        _controller = new MessagesController(
            _messageOperationsService.Object,
            _namespaceRepository.Object,
            _providerRouter,
            _liveTailSessionFactory.Object,
            _liveTailConnectionLimiter.Object,
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    Items = { { "OwnerId", Namespace.SpaOwnerId } }
                }
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

    private void SetIntentHeaders(string intent)
    {
        _controller.ControllerContext.HttpContext.Request.Headers[IntentHeaders.IntentHeaderName] = intent;
        _controller.ControllerContext.HttpContext.Request.Headers[IntentHeaders.ConfirmHeaderName] = "true";
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullMessageOperationsService_ShouldThrow()
    {
        var act = () => new MessagesController(
            null!, _namespaceRepository.Object, _providerRouter,
            _liveTailSessionFactory.Object, _liveTailConnectionLimiter.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_NullLogger_ShouldThrow()
    {
        var act = () => new MessagesController(
            _messageOperationsService.Object, _namespaceRepository.Object, _providerRouter,
            _liveTailSessionFactory.Object, _liveTailConnectionLimiter.Object, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region PeekQueueMessages Tests

    [Fact]
    public async Task PeekQueueMessages_Success_ShouldReturnOk()
    {
        var namespace_ = CreateTestNamespace();
        var messages = new List<Message>
        {
            new() { MessageId = "msg-1", SequenceNumber = 1, EnqueuedTime = DateTimeOffset.UtcNow },
            new() { MessageId = "msg-2", SequenceNumber = 2, EnqueuedTime = DateTimeOffset.UtcNow }
        };

        _namespaceRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(namespace_));
        _messageOperationsService.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));

        var result = await _controller.PeekQueueMessages(namespace_.Id, "my-queue");

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var responses = okResult.Value.Should().BeAssignableTo<List<MessageResponse>>().Subject;
        responses.Should().HaveCount(2);
    }

    [Fact]
    public async Task PeekQueueMessages_Failure_ShouldReturnError()
    {
        var namespace_ = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(namespace_));
        _messageOperationsService.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Failure(Error.ExternalService("SB_ERR", "Service Bus error")));

        var result = await _controller.PeekQueueMessages(namespace_.Id, "my-queue");

        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task PeekQueueMessages_ShouldClampMaxMessages()
    {
        var namespace_ = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(namespace_));
        _messageOperationsService.Setup(r => r.PeekMessagesAsync(
            It.Is<GetMessagesRequest>(req => req.MaxMessages <= 1000 && req.MaxMessages >= 1),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new List<Message>()));

        await _controller.PeekQueueMessages(namespace_.Id, "my-queue", maxMessages: 999);

        _messageOperationsService.Verify(
            r => r.PeekMessagesAsync(
                It.Is<GetMessagesRequest>(req => req.MaxMessages == 999),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion

    #region PeekSubscriptionMessages Tests

    [Fact]
    public async Task PeekSubscriptionMessages_Success_ShouldReturnOk()
    {
        var namespace_ = CreateTestNamespace();
        var messages = new List<Message>
        {
            new() { MessageId = "msg-1", SequenceNumber = 1, EnqueuedTime = DateTimeOffset.UtcNow }
        };

        _namespaceRepository.Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(namespace_));
        _messageOperationsService.Setup(r => r.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));

        var result = await _controller.PeekSubscriptionMessages(namespace_.Id, "my-topic", "my-sub");

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
    }

    #endregion

    #region PeekQueueDeadLetterMessages Tests

    [Fact]
    public async Task PeekQueueDeadLetterMessages_Success_ShouldReturnOk()
    {
        var namespace_ = CreateTestNamespace();
        var messages = new List<Message>
        {
            new() { MessageId = "dlq-msg-1", SequenceNumber = 1, EnqueuedTime = DateTimeOffset.UtcNow, IsFromDeadLetter = true }
        };

        _namespaceRepository.Setup(r => r.GetByIdAsync(namespace_.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(namespace_));
        _messageOperationsService.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));

        var result = await _controller.PeekQueueDeadLetterMessages(namespace_.Id, "my-queue");

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<MessageResponse>>().Subject.ToList();
        
        response.Should().HaveCount(1);
        response[0].MessageId.Should().Be("dlq-msg-1");
        response[0].SequenceNumber.Should().Be(1);
        response[0].IsFromDeadLetter.Should().BeTrue();
    }

    #endregion

    #region PeekSubscriptionDeadLetterMessages Tests

    [Fact]
    public async Task PeekSubscriptionDeadLetterMessages_Success_ShouldReturnOk()
    {
        var namespace_ = CreateTestNamespace();
        var messages = new List<Message>
        {
            new() { MessageId = "dlq-1", SequenceNumber = 1, EnqueuedTime = DateTimeOffset.UtcNow, IsFromDeadLetter = true }
        };

        _namespaceRepository.Setup(r => r.GetByIdAsync(namespace_.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(namespace_));
        _messageOperationsService.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));

        var result = await _controller.PeekSubscriptionDeadLetterMessages(namespace_.Id, "my-topic", "my-sub");

        var okResult = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = okResult.Value.Should().BeAssignableTo<IEnumerable<MessageResponse>>().Subject.ToList();
        
        response.Should().HaveCount(1);
        response[0].MessageId.Should().Be("dlq-1");
        response[0].SequenceNumber.Should().Be(1);
        response[0].IsFromDeadLetter.Should().BeTrue();
    }

    #endregion

    #region ReplayMessage Tests

    [Fact]
    public async Task ReplayMessage_Success_ShouldReturnAccepted()
    {
        SetIntentHeaders(IntentHeaders.IntentReplayMessage);
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _messageOperationsService.Setup(r => r.ReplayMessageAsync(ns.Id, "my-queue", null, 42, It.IsAny<CancellationToken>()))
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
        SetIntentHeaders(IntentHeaders.IntentReplayMessage);
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _messageOperationsService.Setup(r => r.ReplayMessageAsync(ns.Id, "my-topic", "my-sub", 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.ReplayMessage(ns.Id, 42, "my-topic", "my-sub");

        result.Should().BeOfType<AcceptedResult>();
    }

    #endregion

    #region PurgeMessage Tests

    [Fact]
    public async Task PurgeMessage_Success_ShouldReturnAccepted()
    {
        SetIntentHeaders(IntentHeaders.IntentPurgeMessage);
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _messageOperationsService.Setup(r => r.PurgeMessageAsync(ns.Id, "my-queue", null, 42, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var result = await _controller.PurgeMessage(ns.Id, 42, "my-queue", null, fromDeadLetter: true);

        result.Should().BeOfType<AcceptedResult>();
        _messageOperationsService.Verify(r => r.PurgeMessageAsync(ns.Id, "my-queue", null, 42, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PurgeMessage_MissingIntentHeaders_ShouldReturn428()
    {
        var ns = CreateTestNamespace();

        var result = await _controller.PurgeMessage(ns.Id, 42, "my-queue");

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status428PreconditionRequired);
        _messageOperationsService.Verify(
            r => r.PurgeMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PurgeMessage_ProductionNamespace_ShouldReturn403()
    {
        SetIntentHeaders(IntentHeaders.IntentPurgeMessage);
        var ns = Namespace.Create(
            "prod-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Prod NS",
            environment: ServiceHub.Core.Enums.EnvironmentType.Prod).Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var result = await _controller.PurgeMessage(ns.Id, 42, "my-queue");

        var problem = result.Should().BeOfType<ObjectResult>().Subject;
        problem.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        _messageOperationsService.Verify(
            r => r.PurgeMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PurgeMessage_WrongOwner_ShouldReturnNotFound()
    {
        SetIntentHeaders(IntentHeaders.IntentPurgeMessage);
        var ns = Namespace.Create(
            "other-owner-namespace",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Other NS",
            ownerId: "entra:someone-else").Value;
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var result = await _controller.PurgeMessage(ns.Id, 42, "my-queue");

        result.Should().BeOfType<NotFoundResult>();
        _messageOperationsService.Verify(
            r => r.PurgeMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PurgeMessage_OperationFails_ShouldReturnError()
    {
        SetIntentHeaders(IntentHeaders.IntentPurgeMessage);
        var ns = CreateTestNamespace();
        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _messageOperationsService.Setup(r => r.PurgeMessageAsync(ns.Id, "my-queue", null, 42, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.ExternalService("AWS.SQS.PurgeFailed", "SQS error")));

        var result = await _controller.PurgeMessage(ns.Id, 42, "my-queue");

        result.Should().NotBeOfType<AcceptedResult>();
    }

    #endregion
}
