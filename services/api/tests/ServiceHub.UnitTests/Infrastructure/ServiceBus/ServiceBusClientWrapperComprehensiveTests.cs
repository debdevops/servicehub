using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.ServiceBus;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.ServiceBus;

/// <summary>
/// Comprehensive tests for ServiceBusClientWrapper - Critical for Azure integration
/// Coverage target: 80%+ (currently 9.3%)
/// </summary>
public sealed class ServiceBusClientWrapperTests : IAsyncLifetime
{
    private readonly Mock<ServiceBusClient> _serviceBusClientMock = new();
    private readonly Mock<ServiceBusSender> _senderMock = new();
    private readonly Mock<ServiceBusReceiver> _receiverMock = new();
    private readonly ServiceBusClientWrapper _sut;

    private const string TestQueueName = "test-queue";
    private const string TestTopicName = "test-topic";
    private const string TestSubscriptionName = "test-subscription";
    private readonly Guid TestNamespaceId = Guid.NewGuid();
    private readonly string _testConnectionString = "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=test;SharedAccessKey=test";

    public ServiceBusClientWrapperTests()
    {
        _sut = new ServiceBusClientWrapper(
            TestNamespaceId,
            _serviceBusClientMock.Object,
            _testConnectionString,
            NullLogger<ServiceBusClientWrapper>.Instance);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    private GetMessagesRequest CreateRequest(
        string entityName,
        bool fromDeadLetter = false,
        int maxMessages = 10,
        long? fromSequenceNumber = null,
        string? subscriptionName = null) =>
        new(
            NamespaceId: TestNamespaceId,
            EntityName: entityName,
            SubscriptionName: subscriptionName,
            FromDeadLetter: fromDeadLetter,
            MaxMessages: maxMessages,
            FromSequenceNumber: fromSequenceNumber);

    // ═══════════════════════════════════════════════════════════════
    // PeekMessagesAsync - Core message reading functionality
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PeekMessagesAsync_WithValidQueue_ReturnsMessages()
    {
        // Arrange
        var messages = new[]
        {
            ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: new BinaryData("test body 1"),
                messageId: "msg1"),
            ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: new BinaryData("test body 2"),
                messageId: "msg2")
        };

        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Returns(_receiverMock.Object);

        _receiverMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<int>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);

        var request = CreateRequest(TestQueueName, maxMessages: 2);

        // Act
        var result = await _sut.PeekMessagesAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        _serviceBusClientMock.Verify(
            x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()),
            Times.Once);
    }

    [Fact]
    public async Task PeekMessagesAsync_WithEmptyQueue_ReturnsEmpty()
    {
        // Arrange
        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Returns(_receiverMock.Object);

        _receiverMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<int>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceBusReceivedMessage[] { });

        var request = CreateRequest(TestQueueName);

        // Act
        var result = await _sut.PeekMessagesAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task PeekMessagesAsync_WithNullQueueName_ReturnsFailure()
    {
        // Arrange
        var request = CreateRequest(null!);

        // Act
        var result = await _sut.PeekMessagesAsync(request);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }


    // ═══════════════════════════════════════════════════════════════
    // PeekMessagesAsync with DeadLetter - DLQ reading functionality
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PeekMessagesAsync_WithDeadLetterFlag_ReturnsDLQMessages()
    {
        // Arrange
        var dlqMessages = new[]
        {
            ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: new BinaryData("error body 1"),
                messageId: "dlq1"),
            ServiceBusModelFactory.ServiceBusReceivedMessage(
                body: new BinaryData("error body 2"),
                messageId: "dlq2")
        };

        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Returns(_receiverMock.Object);

        _receiverMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<int>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dlqMessages);

        var request = CreateRequest(TestQueueName, fromDeadLetter: true);

        // Act
        var result = await _sut.PeekMessagesAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task PeekMessagesAsync_WithDeadLetterFlag_EmptyDLQ_ReturnsEmpty()
    {
        // Arrange
        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Returns(_receiverMock.Object);

        _receiverMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<int>(), It.IsAny<long?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceBusReceivedMessage[] { });

        var request = CreateRequest(TestQueueName, fromDeadLetter: true);

        // Act
        var result = await _sut.PeekMessagesAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }


    // ═══════════════════════════════════════════════════════════════
    // ReplayMessageAsync - Message replay from DLQ
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReplayMessageAsync_WithValidMessage_SendsSuccessfully()
    {
        // Arrange
        const long sequenceNumber = 1L;
        var targetMessage = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData("replay body"),
            messageId: "test-msg-id",
            sequenceNumber: sequenceNumber);

        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Returns(_receiverMock.Object);

        _serviceBusClientMock
            .Setup(x => x.CreateSender(TestQueueName))
            .Returns(_senderMock.Object);

        _receiverMock
            .Setup(x => x.ReceiveMessagesAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ServiceBusReceivedMessage> { targetMessage });

        _receiverMock
            .Setup(x => x.CompleteMessageAsync(targetMessage, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _senderMock
            .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ReplayMessageAsync(TestQueueName, null, sequenceNumber);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ReplayMessageAsync_WithSubscriptionName_SendsSuccessfully()
    {
        // Arrange
        const string topicName = TestTopicName;
        const string subscriptionName = TestSubscriptionName;
        const long sequenceNumber = 1L;
        var targetMessage = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData("topic replay body"),
            messageId: "topic-msg-id",
            sequenceNumber: sequenceNumber);

        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(topicName, subscriptionName, It.IsAny<ServiceBusReceiverOptions>()))
            .Returns(_receiverMock.Object);

        _serviceBusClientMock
            .Setup(x => x.CreateSender(topicName))
            .Returns(_senderMock.Object);

        _receiverMock
            .Setup(x => x.ReceiveMessagesAsync(It.IsAny<int>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ServiceBusReceivedMessage> { targetMessage });

        _receiverMock
            .Setup(x => x.CompleteMessageAsync(targetMessage, It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _senderMock
            .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await _sut.ReplayMessageAsync(topicName, subscriptionName, sequenceNumber);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }


    // ═══════════════════════════════════════════════════════════════
    // Error Handling
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PeekMessagesAsync_WhenExceptionThrown_ReturnsFailure()
    {
        // Arrange
        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Throws<InvalidOperationException>();

        var request = CreateRequest(TestQueueName);

        // Act & Assert - should handle gracefully instead of throwing
        var result = await _sut.PeekMessagesAsync(request);
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task ReplayMessageAsync_WithNullEntityName_ReturnsFailure()
    {
        // Arrange - ReplayMessageAsync should handle null parameters gracefully

        // Act
        var result = await _sut.ReplayMessageAsync(null!, null, 1L);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

}
