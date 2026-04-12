using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Azure.Messaging.ServiceBus;
using Azure.Messaging.ServiceBus.Administration;
using ServiceHub.Core.DTOs;
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
    private readonly Mock<ServiceBusAdministrationClient> _adminClientMock = new();
    private readonly Mock<ServiceBusSender> _senderMock = new();
    private readonly Mock<ServiceBusReceiver> _receiverMock = new();
    private readonly ServiceBusClientWrapper _sut;

    private const string TestNamespace = "test-namespace";
    private const string TestQueueName = "test-queue";
    private const string TestTopicName = "test-topic";
    private const string TestSubscriptionName = "test-subscription";
    private const Guid TestNamespaceId = default; // Use any Guid for testing

    public ServiceBusClientWrapperTests()
    {
        _sut = new ServiceBusClientWrapper(
            _serviceBusClientMock.Object,
            _adminClientMock.Object,
            NullLogger<ServiceBusClientWrapper>.Instance);
    }

    public Task InitializeAsync() => Task.CompletedTask;
    public Task DisposeAsync() => Task.CompletedTask;

    // ═══════════════════════════════════════════════════════════════
    // PeekMessagesAsync - Core message reading functionality
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PeekMessagesAsync_WithValidQueue_ReturnsMessages()
    {
        // Arrange
        var messages = new[]
        {
            CreateTestServiceBusMessage("msg1", "test body 1"),
            CreateTestServiceBusMessage("msg2", "test body 2")
        };

        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Returns(_receiverMock.Object);

        _receiverMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(messages);

        // Act
        var result = await _sut.PeekMessagesAsync(
            TestNamespaceId, TestQueueName, maxMessages: 2, sequenceNumber: 0);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
        _serviceBusClientMock.Verify(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()), Times.Once);
        _receiverMock.Verify(x => x.PeekMessagesAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task PeekMessagesAsync_WithEmptyQueue_ReturnsEmpty()
    {
        // Arrange
        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Returns(_receiverMock.Object);

        _receiverMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceBusReceivedMessage[] { });

        // Act
        var result = await _sut.PeekMessagesAsync(
            TestNamespaceId, TestQueueName, maxMessages: 10, sequenceNumber: 0);

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task PeekMessagesAsync_WithNullQueueName_ThrowsException()
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _sut.PeekMessagesAsync(TestNamespaceId, null!, maxMessages: 10))
            .Should()
            .ThrowAsync<ArgumentNullException>();
    }

    // ═══════════════════════════════════════════════════════════════
    // PeekDeadLetterMessagesAsync - DLQ reading functionality
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_WithValidQueue_ReturnsDLQMessages()
    {
        // Arrange
        var dlqMessages = new[]
        {
            CreateTestServiceBusMessage("dlq1", "error body 1", "Expired"),
            CreateTestServiceBusMessage("dlq2", "error body 2", "MaxDelivery")
        };

        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Returns(_receiverMock.Object);

        _receiverMock
            .Setup(x => x.PeekDeadLetterMessagesAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(dlqMessages);

        // Act
        var result = await _sut.PeekDeadLetterMessagesAsync(
            TestNamespaceId, TestQueueName, maxMessages: 10, sequenceNumber: 0);

        // Assert
        result.Should().HaveCount(2);
        result.Should().AllSatisfy(msg => msg.DeadLetterReason.Should().NotBeNullOrEmpty());
    }

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_WithEmptyDLQ_ReturnsEmpty()
    {
        // Arrange
        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Returns(_receiverMock.Object);

        _receiverMock
            .Setup(x => x.PeekDeadLetterMessagesAsync(It.IsAny<int>(), It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServiceBusReceivedMessage[] { });

        // Act
        var result = await _sut.PeekDeadLetterMessagesAsync(
            TestNamespaceId, TestQueueName, maxMessages: 10, sequenceNumber: 0);

        // Assert
        result.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // ReplayMessageAsync - Message replay from DLQ
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReplayMessageAsync_WithValidMessage_SendsSuccessfully()
    {
        // Arrange
        var messageId = Guid.NewGuid().ToString();
        var receivedMsg = CreateTestServiceBusMessage(messageId, "replay body");

        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Returns(_receiverMock.Object);

        _serviceBusClientMock
            .Setup(x => x.CreateSender(TestQueueName))
            .Returns(_senderMock.Object);

        _receiverMock
            .Setup(x => x.DeadLetterMessageAsync(It.IsAny<ServiceBusReceivedMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _senderMock
            .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.ReplayMessageAsync(TestNamespaceId, TestQueueName, receivedMsg);

        // Assert
        _senderMock.Verify(
            x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════
    // GetQueuesAsync - Queue listing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetQueuesAsync_WithValidNamespace_ReturnsQueueProperties()
    {
        // Arrange
        var queueProps = new[]
        {
            new QueueProperties(new Azure.Messaging.ServiceBus.Administration.BinaryData("queue1"))
            {
                Name = "queue1",
                ActiveMessageCount = 10,
                DeadLetterMessageCount = 2
            },
            new QueueProperties(new Azure.Messaging.ServiceBus.Administration.BinaryData("queue2"))
            {
                Name = "queue2",
                ActiveMessageCount = 5,
                DeadLetterMessageCount = 0
            }
        };

        _adminClientMock
            .Setup(x => x.GetQueuesRuntimePropertiesAsync(It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableOf(queueProps));

        // Act
        var result = await _sut.GetQueuesAsync(TestNamespaceId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetQueuesAsync_WithNoQueues_ReturnsEmpty()
    {
        // Arrange
        _adminClientMock
            .Setup(x => x.GetQueuesRuntimePropertiesAsync(It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableOf(Array.Empty<QueueRuntimeProperties>()));

        // Act
        var result = await _sut.GetQueuesAsync(TestNamespaceId);

        // Assert
        result.Should().BeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // GetTopicsAsync - Topic listing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetTopicsAsync_WithValidNamespace_ReturnsTopicProperties()
    {
        // Arrange
        var topicProps = new[]
        {
            new TopicProperties(new Azure.Messaging.ServiceBus.Administration.BinaryData("topic1"))
            {
                Name = "topic1",
                SubscriptionCount = 3
            },
            new TopicProperties(new Azure.Messaging.ServiceBus.Administration.BinaryData("topic2"))
            {
                Name = "topic2",
                SubscriptionCount = 1
            }
        };

        _adminClientMock
            .Setup(x => x.GetTopicsRuntimePropertiesAsync(It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableOf(topicProps));

        // Act
        var result = await _sut.GetTopicsAsync(TestNamespaceId);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
    }

    // ═══════════════════════════════════════════════════════════════
    // GetSubscriptionsAsync - Subscription listing
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetSubscriptionsAsync_WithValidTopic_ReturnsSubscriptions()
    {
        // Arrange
        var subProps = new[]
        {
            new SubscriptionProperties(new Azure.Messaging.ServiceBus.Administration.BinaryData("sub1"))
            {
                TopicName = TestTopicName,
                SubscriptionName = "sub1"
            },
            new SubscriptionProperties(new Azure.Messaging.ServiceBus.Administration.BinaryData("sub2"))
            {
                TopicName = TestTopicName,
                SubscriptionName = "sub2"
            }
        };

        _adminClientMock
            .Setup(x => x.GetSubscriptionsRuntimePropertiesAsync(TestTopicName, It.IsAny<CancellationToken>()))
            .Returns(AsyncEnumerableOf(subProps));

        // Act
        var result = await _sut.GetSubscriptionsAsync(TestNamespaceId, TestTopicName);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(2);
    }

    // ═══════════════════════════════════════════════════════════════
    // Error Handling
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PeekMessagesAsync_WhenExceptionThrown_PropagatesException()
    {
        // Arrange
        _serviceBusClientMock
            .Setup(x => x.CreateReceiver(TestQueueName, It.IsAny<ServiceBusReceiverOptions>()))
            .Throws<InvalidOperationException>();

        // Act & Assert
        await FluentActions
            .Invoking(() => _sut.PeekMessagesAsync(TestNamespaceId, TestQueueName, 10))
            .Should()
            .ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task ReplayMessageAsync_WhenSendFails_PropagatesException()
    {
        // Arrange
        var messageId = Guid.NewGuid().ToString();
        var receivedMsg = CreateTestServiceBusMessage(messageId, "test");

        _serviceBusClientMock
            .Setup(x => x.CreateSender(TestQueueName))
            .Returns(_senderMock.Object);

        _senderMock
            .Setup(x => x.SendMessageAsync(It.IsAny<ServiceBusMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync<ServiceBusException>();

        // Act & Assert
        await FluentActions
            .Invoking(() => _sut.ReplayMessageAsync(TestNamespaceId, TestQueueName, receivedMsg))
            .Should()
            .ThrowAsync<ServiceBusException>();
    }

    // ═══════════════════════════════════════════════════════════════
    // Helper Methods
    // ═══════════════════════════════════════════════════════════════

    private static ServiceBusReceivedMessage CreateTestServiceBusMessage(
        string messageId,
        string body,
        string? deadLetterReason = null)
    {
        var msg = ServiceBusModelFactory.ServiceBusReceivedMessage(
            body: new BinaryData(body),
            messageId: messageId,
            deadLetterReason: deadLetterReason);

        return msg;
    }

    private static async IAsyncEnumerable<T> AsyncEnumerableOf<T>(T[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }
}

namespace ServiceHub.UnitTests.Infrastructure.ServiceBus;

/// <summary>
/// Tests for MessageReceiver - Service Bus message receiving operations
/// Coverage target: 50%+ (currently 0%)
/// </summary>
public sealed class MessageReceiverTests
{
    private readonly Mock<ServiceBusClientWrapper> _clientMock = new();
    private readonly MessageReceiver _sut;

    public MessageReceiverTests()
    {
        _sut = new MessageReceiver(_clientMock.Object);
    }

    [Fact]
    public async Task PeekMessagesAsync_WithValidParameters_CallsClientMethod()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        var queueName = "test-queue";
        var messages = new List<ServiceBusReceivedMessage>();

        _clientMock
            .Setup(x => x.PeekMessagesAsync(namespaceId, queueName, It.IsAny<int>(), It.IsAny<long>()))
            .ReturnsAsync(messages);

        // Act
        var result = await _sut.PeekMessagesAsync(namespaceId, queueName, maxMessages: 10, sequenceNumber: 0);

        // Assert
        result.Should().NotBeNull();
        _clientMock.Verify(x => x.PeekMessagesAsync(namespaceId, queueName, 10, 0), Times.Once);
    }
}

namespace ServiceHub.UnitTests.Infrastructure.ServiceBus;

/// <summary>
/// Tests for MessageSender - Service Bus message sending operations
/// Coverage target: 50%+ (currently 0%)
/// </summary>
public sealed class MessageSenderTests
{
    private readonly Mock<ServiceBusClientWrapper> _clientMock = new();
    private readonly MessageSender _sut;

    public MessageSenderTests()
    {
        _sut = new MessageSender(_clientMock.Object);
    }

    [Fact]
    public async Task SendAsync_WithValidMessage_CallsClientMethod()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        var queueName = "test-queue";
        var request = new SendMessageRequest(
            namespaceId,
            queueName,
            "test body");

        _clientMock
            .Setup(x => x.SendMessageAsync(namespaceId, queueName, It.IsAny<string>(), null, null, null, null, null, null, null, null, null, null, null))
            .Returns(Task.CompletedTask);

        // Act
        await _sut.SendAsync(request);

        // Assert
        _clientMock.Verify(x => x.SendMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), 
            null, null, null, null, null, null, null, null, null, null, null), Times.Once);
    }
}
