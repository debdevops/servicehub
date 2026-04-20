using Azure.Messaging.ServiceBus;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Infrastructure.ServiceBus;
using ServiceHub.Shared.Constants;

namespace ServiceHub.UnitTests.Infrastructure.ServiceBus;

/// <summary>
/// Unit tests for ScheduleMessageAsync and CancelScheduledMessageAsync
/// on ServiceBusClientWrapper.
/// </summary>
public sealed class ServiceBusClientWrapperScheduledTests
{
    private const string ValidConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc123=";

    private static ServiceBusClientWrapper CreateWrapper(Mock<ServiceBusClient> clientMock)
        => new(
            Guid.NewGuid(),
            clientMock.Object,
            ValidConnectionString,
            NullLogger<ServiceBusClientWrapper>.Instance);

    private static SendMessageRequest ValidRequest(string entityName = "test-queue") =>
        new(
            EntityName: entityName,
            Body: "{\"test\":true}",
            ContentType: "application/json",
            CorrelationId: null,
            SessionId: null,
            PartitionKey: null,
            Subject: null,
            ReplyTo: null,
            ReplyToSessionId: null,
            To: null,
            TimeToLiveSeconds: null,
            ScheduledEnqueueTimeUtc: null,
            ApplicationProperties: null);

    // ═══════════════════════════════════════════════════════════════
    // ScheduleMessageAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ScheduleMessageAsync_Success_ReturnsSequenceNumber()
    {
        // Arrange
        var senderMock = new Mock<ServiceBusSender>();
        senderMock
            .Setup(s => s.ScheduleMessageAsync(
                It.IsAny<ServiceBusMessage>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(42L);

        var clientMock = new Mock<ServiceBusClient>();
        clientMock
            .Setup(c => c.CreateSender("test-queue"))
            .Returns(senderMock.Object);
        clientMock.SetupGet(c => c.IsClosed).Returns(false);

        var wrapper = CreateWrapper(clientMock);
        var scheduledTime = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        var result = await wrapper.ScheduleMessageAsync(ValidRequest(), scheduledTime);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42L);
    }

    [Fact]
    public async Task ScheduleMessageAsync_ClientDisposed_ReturnsServiceUnavailable()
    {
        // Arrange
        var clientMock = new Mock<ServiceBusClient>();
        clientMock.SetupGet(c => c.IsClosed).Returns(false);

        var wrapper = CreateWrapper(clientMock);
        await wrapper.DisposeAsync();

        // Act
        var result = await wrapper.ScheduleMessageAsync(ValidRequest(), DateTimeOffset.UtcNow.AddHours(1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.General.ServiceUnavailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ScheduleMessageAsync_NullOrEmptyEntityName_ReturnsValidationError(string? entityName)
    {
        // Arrange
        var clientMock = new Mock<ServiceBusClient>();
        clientMock.SetupGet(c => c.IsClosed).Returns(false);

        var wrapper = CreateWrapper(clientMock);
        var request = ValidRequest(entityName!);

        // Act
        var result = await wrapper.ScheduleMessageAsync(request, DateTimeOffset.UtcNow.AddHours(1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Message.QueueNameRequired);
    }

    [Fact]
    public async Task ScheduleMessageAsync_EntityNotFound_ReturnsNotFound()
    {
        // Arrange
        var senderMock = new Mock<ServiceBusSender>();
        senderMock
            .Setup(s => s.ScheduleMessageAsync(
                It.IsAny<ServiceBusMessage>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("not found", ServiceBusFailureReason.MessagingEntityNotFound));

        var clientMock = new Mock<ServiceBusClient>();
        clientMock.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(senderMock.Object);
        clientMock.SetupGet(c => c.IsClosed).Returns(false);

        var wrapper = CreateWrapper(clientMock);

        // Act
        var result = await wrapper.ScheduleMessageAsync(ValidRequest(), DateTimeOffset.UtcNow.AddHours(1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Queue.NotFound);
    }

    [Fact]
    public async Task ScheduleMessageAsync_ServiceBusException_ReturnsSendFailed()
    {
        // Arrange
        var senderMock = new Mock<ServiceBusSender>();
        senderMock
            .Setup(s => s.ScheduleMessageAsync(
                It.IsAny<ServiceBusMessage>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("quota exceeded", ServiceBusFailureReason.QuotaExceeded));

        var clientMock = new Mock<ServiceBusClient>();
        clientMock.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(senderMock.Object);
        clientMock.SetupGet(c => c.IsClosed).Returns(false);

        var wrapper = CreateWrapper(clientMock);

        // Act
        var result = await wrapper.ScheduleMessageAsync(ValidRequest(), DateTimeOffset.UtcNow.AddHours(1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Message.SendFailed);
    }

    [Fact]
    public async Task ScheduleMessageAsync_UnexpectedException_ReturnsUnexpectedError()
    {
        // Arrange
        var senderMock = new Mock<ServiceBusSender>();
        senderMock
            .Setup(s => s.ScheduleMessageAsync(
                It.IsAny<ServiceBusMessage>(),
                It.IsAny<DateTimeOffset>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("unexpected failure"));

        var clientMock = new Mock<ServiceBusClient>();
        clientMock.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(senderMock.Object);
        clientMock.SetupGet(c => c.IsClosed).Returns(false);

        var wrapper = CreateWrapper(clientMock);

        // Act
        var result = await wrapper.ScheduleMessageAsync(ValidRequest(), DateTimeOffset.UtcNow.AddHours(1));

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.General.UnexpectedError);
    }

    // ═══════════════════════════════════════════════════════════════
    // CancelScheduledMessageAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task CancelScheduledMessageAsync_Success_ReturnsOk()
    {
        // Arrange
        var senderMock = new Mock<ServiceBusSender>();
        senderMock
            .Setup(s => s.CancelScheduledMessageAsync(
                42L,
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clientMock = new Mock<ServiceBusClient>();
        clientMock.Setup(c => c.CreateSender("test-queue")).Returns(senderMock.Object);
        clientMock.SetupGet(c => c.IsClosed).Returns(false);

        var wrapper = CreateWrapper(clientMock);

        // Act
        var result = await wrapper.CancelScheduledMessageAsync("test-queue", 42L);

        // Assert
        result.IsSuccess.Should().BeTrue();
        senderMock.Verify(s => s.CancelScheduledMessageAsync(42L, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CancelScheduledMessageAsync_ClientDisposed_ReturnsServiceUnavailable()
    {
        // Arrange
        var clientMock = new Mock<ServiceBusClient>();
        clientMock.SetupGet(c => c.IsClosed).Returns(false);

        var wrapper = CreateWrapper(clientMock);
        await wrapper.DisposeAsync();

        // Act
        var result = await wrapper.CancelScheduledMessageAsync("test-queue", 1L);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.General.ServiceUnavailable);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CancelScheduledMessageAsync_NullOrEmptyEntityName_ReturnsValidationError(string? entityName)
    {
        // Arrange
        var clientMock = new Mock<ServiceBusClient>();
        clientMock.SetupGet(c => c.IsClosed).Returns(false);

        var wrapper = CreateWrapper(clientMock);

        // Act
        var result = await wrapper.CancelScheduledMessageAsync(entityName!, 1L);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Message.QueueNameRequired);
    }

    [Fact]
    public async Task CancelScheduledMessageAsync_EntityNotFound_ReturnsNotFound()
    {
        // Arrange
        var senderMock = new Mock<ServiceBusSender>();
        senderMock
            .Setup(s => s.CancelScheduledMessageAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("not found", ServiceBusFailureReason.MessagingEntityNotFound));

        var clientMock = new Mock<ServiceBusClient>();
        clientMock.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(senderMock.Object);
        clientMock.SetupGet(c => c.IsClosed).Returns(false);

        var wrapper = CreateWrapper(clientMock);

        // Act
        var result = await wrapper.CancelScheduledMessageAsync("test-queue", 1L);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Queue.NotFound);
    }

    [Fact]
    public async Task CancelScheduledMessageAsync_MessageNotFound_ReturnsMessageNotFound()
    {
        // Arrange
        var senderMock = new Mock<ServiceBusSender>();
        senderMock
            .Setup(s => s.CancelScheduledMessageAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ServiceBusException("message not found", ServiceBusFailureReason.MessageNotFound));

        var clientMock = new Mock<ServiceBusClient>();
        clientMock.Setup(c => c.CreateSender(It.IsAny<string>())).Returns(senderMock.Object);
        clientMock.SetupGet(c => c.IsClosed).Returns(false);

        var wrapper = CreateWrapper(clientMock);

        // Act
        var result = await wrapper.CancelScheduledMessageAsync("test-queue", 999L);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Message.NotFound);
    }
}
