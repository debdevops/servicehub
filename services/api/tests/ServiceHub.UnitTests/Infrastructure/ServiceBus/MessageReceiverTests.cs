using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Infrastructure.ServiceBus;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.ServiceBus;

public sealed class MessageReceiverTests
{
    private readonly Mock<IServiceBusClientCache> _clientCacheMock = new();
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly Mock<IConnectionStringProtector> _connectionStringProtectorMock = new();
    private readonly Mock<IServiceBusClientWrapper> _clientWrapperMock = new();
    private readonly MessageReceiver _sut;

    private const string ValidConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=TestPolicy;SharedAccessKey=abc123=";
    private const string EncryptedConnectionString = "ENCRYPTED_VALUE";

    public MessageReceiverTests()
    {
        _clientCacheMock
            .Setup(x => x.GetOrCreate(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(_clientWrapperMock.Object);

        _connectionStringProtectorMock
            .Setup(x => x.Unprotect(It.IsAny<string>()))
            .Returns(Result.Success(ValidConnectionString));

        _clientWrapperMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Message>>(new List<Message>()));

        _sut = new MessageReceiver(
            _clientCacheMock.Object,
            _namespaceRepositoryMock.Object,
            _connectionStringProtectorMock.Object,
            NullLogger<MessageReceiver>.Instance);
    }

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullClientCache_Throws()
    {
        var act = () => new MessageReceiver(
            null!,
            _namespaceRepositoryMock.Object,
            _connectionStringProtectorMock.Object,
            NullLogger<MessageReceiver>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientCache");
    }

    [Fact]
    public void Constructor_NullNamespaceRepository_Throws()
    {
        var act = () => new MessageReceiver(
            _clientCacheMock.Object,
            null!,
            _connectionStringProtectorMock.Object,
            NullLogger<MessageReceiver>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullConnectionStringProtector_Throws()
    {
        var act = () => new MessageReceiver(
            _clientCacheMock.Object,
            _namespaceRepositoryMock.Object,
            null!,
            NullLogger<MessageReceiver>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionStringProtector");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new MessageReceiver(
            _clientCacheMock.Object,
            _namespaceRepositoryMock.Object,
            _connectionStringProtectorMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ═══════════════════════════════════════════════════════════════
    // PeekMessagesAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PeekMessagesAsync_EmptyNamespaceId_ReturnsValidationError()
    {
        // Arrange
        var request = new GetMessagesRequest(
            NamespaceId: Guid.Empty,
            EntityName: "queue",
            SubscriptionName: null,
            FromDeadLetter: false,
            MaxMessages: 10,
            FromSequenceNumber: null);

        // Act
        var result = await _sut.PeekMessagesAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Namespace.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PeekMessagesAsync_NullOrWhiteSpaceEntityName_ReturnsValidationError(string entityName)
    {
        // Arrange
        var request = new GetMessagesRequest(
            NamespaceId: Guid.NewGuid(),
            EntityName: entityName,
            SubscriptionName: null,
            FromDeadLetter: false,
            MaxMessages: 10,
            FromSequenceNumber: null);

        // Act
        var result = await _sut.PeekMessagesAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Message.QueueNameRequired);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1001)]
    public async Task PeekMessagesAsync_MaxMessagesOutOfRange_ReturnsValidationError(int maxMessages)
    {
        // Arrange
        var request = new GetMessagesRequest(
            NamespaceId: Guid.NewGuid(),
            EntityName: "queue",
            SubscriptionName: null,
            FromDeadLetter: false,
            MaxMessages: maxMessages,
            FromSequenceNumber: null);

        // Act
        var result = await _sut.PeekMessagesAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.General.InvalidRequest);
    }

    [Fact]
    public async Task PeekMessagesAsync_ValidRequest_ReturnsMessages()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        var messages = new List<Message>();

        _clientWrapperMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Message>>(messages));

        var request = new GetMessagesRequest(
            NamespaceId: @namespace.Id,
            EntityName: "queue",
            SubscriptionName: null,
            FromDeadLetter: false,
            MaxMessages: 10,
            FromSequenceNumber: null);

        // Act
        var result = await _sut.PeekMessagesAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(0);
    }

    [Fact]
    public async Task PeekMessagesAsync_NamespaceNotFound_ReturnsFailure()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        var error = Error.NotFound("Namespace", "Not found");
        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(error));

        var request = new GetMessagesRequest(
            NamespaceId: namespaceId,
            EntityName: "queue",
            SubscriptionName: null,
            FromDeadLetter: false,
            MaxMessages: 10,
            FromSequenceNumber: null);

        // Act
        var result = await _sut.PeekMessagesAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // PeekDeadLetterMessagesAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_ValidRequest_ReturnsDeadLetterMessages()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        var messages = new List<Message>();

        _clientWrapperMock
            .Setup(x => x.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Message>>(messages));

        var request = new GetMessagesRequest(
            NamespaceId: @namespace.Id,
            EntityName: "queue",
            SubscriptionName: null,
            FromDeadLetter: true,
            MaxMessages: 10,
            FromSequenceNumber: null);

        // Act
        var result = await _sut.PeekDeadLetterMessagesAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(0);
    }

    [Fact]
    public async Task PeekDeadLetterMessagesAsync_EmptyNamespaceId_ReturnsValidationError()
    {
        // Arrange
        var request = new GetMessagesRequest(
            NamespaceId: Guid.Empty,
            EntityName: "queue",
            SubscriptionName: null,
            FromDeadLetter: true,
            MaxMessages: 10,
            FromSequenceNumber: null);

        // Act
        var result = await _sut.PeekDeadLetterMessagesAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Namespace.NotFound);
    }

    // ═══════════════════════════════════════════════════════════════
    // GetMessageCountAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetMessageCountAsync_EmptyNamespaceId_ReturnsValidationError()
    {
        // Act
        var result = await _sut.GetMessageCountAsync(Guid.Empty, "queue");

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Namespace.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetMessageCountAsync_NullOrWhiteSpaceEntityName_ReturnsValidationError(string? entityName)
    {
        // Act
        var result = await _sut.GetMessageCountAsync(Guid.NewGuid(), entityName!);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Message.QueueNameRequired);
    }

    [Fact]
    public async Task GetMessageCountAsync_Queue_ReturnsActiveMessageCount()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        var queueDto = new QueueRuntimePropertiesDto(
            Name: "my-queue", ActiveMessageCount: 42, DeadLetterMessageCount: 3,
            ScheduledMessageCount: 0, TransferMessageCount: 0, TransferDeadLetterMessageCount: 0,
            SizeInBytes: 1024, Status: "Active", CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow, AccessedAt: DateTimeOffset.UtcNow,
            RequiresSession: false, RequiresDuplicateDetection: false, EnablePartitioning: false,
            EnableBatchedOperations: true, MaxSizeInMegabytes: 1024, MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.MaxValue, LockDuration: TimeSpan.FromMinutes(1),
            AutoDeleteOnIdle: TimeSpan.MaxValue);

        _clientWrapperMock
            .Setup(x => x.GetQueueAsync("my-queue", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(queueDto));

        // Act
        var result = await _sut.GetMessageCountAsync(@namespace.Id, "my-queue");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
    }

    [Fact]
    public async Task GetMessageCountAsync_Subscription_ReturnsActiveMessageCount()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        var subDto = new SubscriptionRuntimePropertiesDto(
            Name: "sub1", TopicName: "my-topic", ActiveMessageCount: 15,
            DeadLetterMessageCount: 1, TransferMessageCount: 0, TransferDeadLetterMessageCount: 0,
            Status: "Active", CreatedAt: DateTimeOffset.UtcNow, UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow, RequiresSession: false, EnableBatchedOperations: true,
            EnableDeadLetteringOnMessageExpiration: false, EnableDeadLetteringOnFilterEvaluationExceptions: false,
            MaxDeliveryCount: 10, DefaultMessageTimeToLive: TimeSpan.MaxValue,
            LockDuration: TimeSpan.FromMinutes(1), AutoDeleteOnIdle: TimeSpan.MaxValue,
            ForwardTo: null, ForwardDeadLetteredMessagesTo: null);

        _clientWrapperMock
            .Setup(x => x.GetSubscriptionAsync("my-topic", "sub1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(subDto));

        // Act
        var result = await _sut.GetMessageCountAsync(@namespace.Id, "my-topic", "sub1");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(15);
    }

    // ═══════════════════════════════════════════════════════════════
    // DeadLetterMessagesAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task DeadLetterMessagesAsync_EmptyNamespaceId_ReturnsValidationError()
    {
        // Arrange
        var request = new DeadLetterRequest(
            NamespaceId: Guid.Empty,
            EntityName: "queue",
            SubscriptionName: null);

        // Act
        var result = await _sut.DeadLetterMessagesAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Namespace.NotFound);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DeadLetterMessagesAsync_NullOrWhiteSpaceEntityName_ReturnsValidationError(string entityName)
    {
        // Arrange
        var request = new DeadLetterRequest(
            NamespaceId: Guid.NewGuid(),
            EntityName: entityName,
            SubscriptionName: null);

        // Act
        var result = await _sut.DeadLetterMessagesAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Message.QueueNameRequired);
    }

    [Fact]
    public async Task DeadLetterMessagesAsync_ValidRequest_ReturnsSuccessWithCount()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        _clientWrapperMock
            .Setup(x => x.DeadLetterMessagesAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);

        var request = new DeadLetterRequest(
            NamespaceId: @namespace.Id,
            EntityName: "queue",
            SubscriptionName: null,
            MessageCount: 5,
            Reason: "test",
            ErrorDescription: "test error");

        // Act
        var result = await _sut.DeadLetterMessagesAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(5);
    }

    // ═══════════════════════════════════════════════════════════════
    // ReplayMessageAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ReplayMessageAsync_EmptyNamespaceId_ReturnsValidationError()
    {
        // Act
        var result = await _sut.ReplayMessageAsync(Guid.Empty, "queue", null, 1);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Namespace.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task ReplayMessageAsync_NullOrWhiteSpaceEntityName_ReturnsValidationError(string? entityName)
    {
        // Act
        var result = await _sut.ReplayMessageAsync(Guid.NewGuid(), entityName!, null, 1);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Message.QueueNameRequired);
    }

    [Fact]
    public async Task ReplayMessageAsync_ValidRequest_SuccessfullyReplaysMessage()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        _clientWrapperMock
            .Setup(x => x.ReplayMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _sut.ReplayMessageAsync(@namespace.Id, "queue", null, 123);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // PurgeMessageAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task PurgeMessageAsync_EmptyNamespaceId_ReturnsValidationError()
    {
        // Act
        var result = await _sut.PurgeMessageAsync(Guid.Empty, "queue", null, 1, false);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Namespace.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task PurgeMessageAsync_NullOrWhiteSpaceEntityName_ReturnsValidationError(string? entityName)
    {
        // Act
        var result = await _sut.PurgeMessageAsync(Guid.NewGuid(), entityName!, null, 1, false);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Message.QueueNameRequired);
    }

    [Fact]
    public async Task PurgeMessageAsync_ValidRequest_SuccessfullyPurgesMessage()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        _clientWrapperMock
            .Setup(x => x.PurgeMessageAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<long>(),
                It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        // Act
        var result = await _sut.PurgeMessageAsync(@namespace.Id, "queue", null, 123, false);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // GetScheduledMessagesAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task GetScheduledMessagesAsync_EmptyNamespaceId_ReturnsValidationError()
    {
        // Act
        var result = await _sut.GetScheduledMessagesAsync(Guid.Empty, "queue", null, 10);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Namespace.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetScheduledMessagesAsync_NullOrWhiteSpaceEntityName_ReturnsValidationError(string? entityName)
    {
        // Act
        var result = await _sut.GetScheduledMessagesAsync(Guid.NewGuid(), entityName!, null, 10);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Message.QueueNameRequired);
    }

    [Fact]
    public async Task GetScheduledMessagesAsync_ValidRequest_ReturnsScheduledMessages()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        var messages = new List<Message>();

        _clientWrapperMock
            .Setup(x => x.GetScheduledMessagesAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<Message>>(messages));

        // Act
        var result = await _sut.GetScheduledMessagesAsync(@namespace.Id, "queue", null, 10);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(0);
    }
}
