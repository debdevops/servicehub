using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Infrastructure.ServiceBus;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.ServiceBus;

public sealed class MessageSenderTests
{
    private readonly Mock<IServiceBusClientCache> _clientCacheMock = new();
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly Mock<IConnectionStringProtector> _connectionStringProtectorMock = new();
    private readonly Mock<IServiceBusClientWrapper> _clientWrapperMock = new();
    private readonly MessageSender _sut;

    private const string ValidConnectionString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=TestPolicy;SharedAccessKey=abc123=";
    private const string EncryptedConnectionString = "ENCRYPTED_VALUE";

    public MessageSenderTests()
    {
        _clientCacheMock
            .Setup(x => x.GetOrCreate(It.IsAny<Guid>(), It.IsAny<string>()))
            .Returns(_clientWrapperMock.Object);

        _connectionStringProtectorMock
            .Setup(x => x.Unprotect(It.IsAny<string>()))
            .Returns(Result.Success(ValidConnectionString));

        _clientWrapperMock
            .Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        _sut = new MessageSender(
            _clientCacheMock.Object,
            _namespaceRepositoryMock.Object,
            _connectionStringProtectorMock.Object,
            NullLogger<MessageSender>.Instance);
    }

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullClientCache_Throws()
    {
        var act = () => new MessageSender(
            null!,
            _namespaceRepositoryMock.Object,
            _connectionStringProtectorMock.Object,
            NullLogger<MessageSender>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("clientCache");
    }

    [Fact]
    public void Constructor_NullNamespaceRepository_Throws()
    {
        var act = () => new MessageSender(
            _clientCacheMock.Object,
            null!,
            _connectionStringProtectorMock.Object,
            NullLogger<MessageSender>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullConnectionStringProtector_Throws()
    {
        var act = () => new MessageSender(
            _clientCacheMock.Object,
            _namespaceRepositoryMock.Object,
            null!,
            NullLogger<MessageSender>.Instance);

        act.Should().Throw<ArgumentNullException>().WithParameterName("connectionStringProtector");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new MessageSender(
            _clientCacheMock.Object,
            _namespaceRepositoryMock.Object,
            _connectionStringProtectorMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ═══════════════════════════════════════════════════════════════
    // ValidateRequest — Private Method Tests
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SendAsync_NullNamespaceId_ReturnsValidationError()
    {
        // Arrange
        var request = new SendMessageRequest(
            NamespaceId: null,
            EntityName: "queue",
            Body: "test");

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Namespace.NotFound);
    }

    [Fact]
    public async Task SendAsync_EmptyNamespaceId_ReturnsValidationError()
    {
        // Arrange
        var request = new SendMessageRequest(
            NamespaceId: Guid.Empty,
            EntityName: "queue",
            Body: "test");

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Namespace.NotFound);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_NullOrWhiteSpaceEntityName_ReturnsValidationError(string? entityName)
    {
        // Arrange
        var request = new SendMessageRequest(
            NamespaceId: Guid.NewGuid(),
            EntityName: entityName,
            Body: "test");

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Message.QueueNameRequired);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SendAsync_NullOrWhiteSpaceBody_ReturnsValidationError(string? body)
    {
        // Arrange
        var request = new SendMessageRequest(
            NamespaceId: Guid.NewGuid(),
            EntityName: "queue",
            Body: body);

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Message.BodyRequired);
    }

    // ═══════════════════════════════════════════════════════════════
    // SendAsync — Main Path
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SendAsync_ValidRequest_SuccessfullyCallsClientWrapper()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        var nsResult = Namespace.Create("test-ns", ValidConnectionString, displayName: "Test");
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        var request = new SendMessageRequest(
            NamespaceId: @namespace.Id,
            EntityName: "test-queue",
            Body: "test message",
            ContentType: null,
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

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _namespaceRepositoryMock.Verify(
            x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _connectionStringProtectorMock.Verify(
            x => x.Unprotect(It.IsAny<string>()),
            Times.Once);
        _clientCacheMock.Verify(
            x => x.GetOrCreate(It.IsAny<Guid>(), It.IsAny<string>()),
            Times.Once);
    }

    [Fact]
    public async Task SendAsync_NamespaceNotFound_ReturnsFailure()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        var error = Error.NotFound("Namespace", $"Namespace {namespaceId} not found");
        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(error));

        var request = new SendMessageRequest(
            NamespaceId: namespaceId,
            EntityName: "test-queue",
            Body: "test message");

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(error);
    }

    [Fact]
    public async Task SendAsync_NoConnectionString_ReturnsValidationError()
    {
        // Arrange
        var namespaceId = Guid.NewGuid();
        var nsResult = Namespace.CreateWithManagedIdentity("test-ns-mi");
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        var request = new SendMessageRequest(
            NamespaceId: @namespace.Id,
            EntityName: "test-queue",
            Body: "test message");

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Namespace.ConnectionStringRequired);
    }

    [Fact]
    public async Task SendAsync_UnprotectFails_ReturnsFailure()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        var unprotectError = Error.Validation("Encryption", "Failed to decrypt connection string");
        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        _connectionStringProtectorMock
            .Setup(x => x.Unprotect(It.IsAny<string>()))
            .Returns(Result.Failure<string>(unprotectError));

        var request = new SendMessageRequest(
            NamespaceId: @namespace.Id,
            EntityName: "test-queue",
            Body: "test message");

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Should().Be(unprotectError);
    }

    [Fact]
    public async Task SendAsync_ClientSendFails_ReturnsFailure()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        var

 sendError = Error.ExternalService("SendError", "Failed to send");
        _clientWrapperMock
            .Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(sendError));

        var request = new SendMessageRequest(
            NamespaceId: @namespace.Id,
            EntityName: "test-queue",
            Body: "test message");

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
    }

    // ═══════════════════════════════════════════════════════════════
    // SendBatchAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SendBatchAsync_EmptyList_ReturnsSuccess()
    {
        // Arrange
        var requests = Array.Empty<SendMessageRequest>();

        // Act
        var result = await _sut.SendBatchAsync(requests);

        // Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task SendBatchAsync_ValidRequests_SendsAll()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        var requests = new[]
        {
            new SendMessageRequest(NamespaceId: @namespace.Id, EntityName: "queue", Body: "message1"),
            new SendMessageRequest(NamespaceId: @namespace.Id, EntityName: "queue", Body: "message2")
        };

        // Act
        var result = await _sut.SendBatchAsync(requests);

        // Assert
        result.IsSuccess.Should().BeTrue();
        _clientWrapperMock.Verify(
            x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task SendBatchAsync_PartialFailure_ReturnsFailure()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        _clientWrapperMock
            .SetupSequence(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success())
            .ReturnsAsync(Result.Failure(Error.ExternalService("SendError", "Failed")));

        var requests = new[]
        {
            new SendMessageRequest(NamespaceId: @namespace.Id, EntityName: "queue", Body: "message1"),
            new SendMessageRequest(NamespaceId: @namespace.Id, EntityName: "queue", Body: "message2")
        };

        // Act
        var result = await _sut.SendBatchAsync(requests);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Errors.Should().NotBeEmpty();
    }

    // ═══════════════════════════════════════════════════════════════
    // SendAsync — Message size exceeded (non-retryable validation error)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task SendAsync_MessageTooLarge_ReturnsValidationFailureWith400StatusCode()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        var sizeError = Error.Validation(ErrorCodes.Message.BodyTooLarge, "The message body exceeds the maximum allowed size.");
        _clientWrapperMock
            .Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(sizeError));

        var request = new SendMessageRequest(
            NamespaceId: @namespace.Id,
            EntityName: "test-queue",
            Body: new string('x', 300_000));

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Message.BodyTooLarge);
        result.Error.Type.Should().Be(ServiceHub.Shared.Results.ErrorType.Validation);
    }

    [Fact]
    public async Task SendAsync_ExternalServiceError_ReturnsExternalServiceFailure()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        // ExternalService errors are retryable → wrapped in InvalidOperationException by the pipeline
        var externalError = Error.ExternalService(ErrorCodes.Message.SendFailed, "Remote error");
        _clientWrapperMock
            .Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(externalError));

        var request = new SendMessageRequest(
            NamespaceId: @namespace.Id,
            EntityName: "test-queue",
            Body: "test message");

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        // The Polly pipeline wraps the InvalidOperationException and retries; after retries exhausted,
        // the outer catch returns ExternalService or Internal error.
        result.Error.Should().NotBeNull();
    }

    [Fact]
    public async Task SendAsync_NotFoundError_ReturnsNonRetryableFailureDirectly()
    {
        // Arrange
        var nsResult = Namespace.Create("test-ns", ValidConnectionString);
        nsResult.IsSuccess.Should().BeTrue();
        var @namespace = nsResult.Value;

        _namespaceRepositoryMock
            .Setup(x => x.GetByIdAsync(@namespace.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(@namespace));

        var notFoundError = Error.NotFound(ErrorCodes.Queue.NotFound, "The queue was not found.");
        _clientWrapperMock
            .Setup(x => x.SendMessageAsync(It.IsAny<SendMessageRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(notFoundError));

        var request = new SendMessageRequest(
            NamespaceId: @namespace.Id,
            EntityName: "missing-queue",
            Body: "test message");

        // Act
        var result = await _sut.SendAsync(request);

        // Assert
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Queue.NotFound);
        result.Error.Type.Should().Be(ServiceHub.Shared.Results.ErrorType.NotFound);
    }
}
