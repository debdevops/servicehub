using FluentAssertions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.LiveTail;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.LiveTail;

public sealed class LiveTailSessionTests
{
    private readonly Mock<IMessageOperationsService> _messageOperationsMock = new();
    private static readonly Guid NamespaceId = Guid.NewGuid();

    private LiveTailSession CreateSut(string entityName = "orders", string? subscriptionName = null, bool fromDeadLetter = false) =>
        new(_messageOperationsMock.Object, NamespaceId, entityName, subscriptionName, fromDeadLetter);

    private static Message BuildMessage(string messageId, long sequenceNumber = 1) => new()
    {
        MessageId = messageId,
        SequenceNumber = sequenceNumber,
        Body = "{}",
        EnqueuedTime = DateTimeOffset.UtcNow,
    };

    // ── Constructor ──────────────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullMessageOperationsService_Throws()
    {
        var act = () => new LiveTailSession(null!, NamespaceId, "orders", null, false);
        act.Should().Throw<ArgumentNullException>().WithParameterName("messageOperationsService");
    }

    [Fact]
    public void Constructor_NullEntityName_Throws()
    {
        var act = () => new LiveTailSession(_messageOperationsMock.Object, NamespaceId, null!, null, false);
        act.Should().Throw<ArgumentException>().WithParameterName("entityName");
    }

    // ── First poll seeds without emitting ───────────────────────────────────

    [Fact]
    public async Task PollNextAsync_FirstPoll_DoesNotEmitExistingBacklog()
    {
        _messageOperationsMock
            .Setup(m => m.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([BuildMessage("m1"), BuildMessage("m2")]));

        var sut = CreateSut();

        var result = await sut.PollNextAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task PollNextAsync_SecondPoll_EmitsOnlyNewMessages()
    {
        _messageOperationsMock
            .SetupSequence(m => m.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([BuildMessage("m1")]))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([BuildMessage("m1"), BuildMessage("m2")]));

        var sut = CreateSut();
        await sut.PollNextAsync();

        var second = await sut.PollNextAsync();

        second.IsSuccess.Should().BeTrue();
        second.Value.Should().ContainSingle().Which.MessageId.Should().Be("m2");
    }

    [Fact]
    public async Task PollNextAsync_SameMessageAcrossPolls_NotReEmitted()
    {
        _messageOperationsMock
            .Setup(m => m.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([BuildMessage("m1")]));

        var sut = CreateSut();
        await sut.PollNextAsync();
        var second = await sut.PollNextAsync();
        var third = await sut.PollNextAsync();

        second.Value.Should().BeEmpty();
        third.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task PollNextAsync_NoMessageId_FallsBackToSequenceNumberKey()
    {
        _messageOperationsMock
            .SetupSequence(m => m.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([BuildMessage("", sequenceNumber: 42)]))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([BuildMessage("", sequenceNumber: 42), BuildMessage("", sequenceNumber: 43)]));

        var sut = CreateSut();
        await sut.PollNextAsync();

        var second = await sut.PollNextAsync();

        second.Value.Should().ContainSingle().Which.SequenceNumber.Should().Be(43);
    }

    // ── Failure passthrough ──────────────────────────────────────────────────

    [Fact]
    public async Task PollNextAsync_PeekFails_ReturnsFailureWithoutThrowing()
    {
        _messageOperationsMock
            .Setup(m => m.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Failure(Error.ExternalService("Provider.Error", "peek failed")));

        var sut = CreateSut();

        var result = await sut.PollNextAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Be("peek failed");
    }

    [Fact]
    public async Task PollNextAsync_FailureThenRecovery_StateIsPreserved()
    {
        _messageOperationsMock
            .SetupSequence(m => m.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([BuildMessage("m1")]))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Failure(Error.ExternalService("Provider.Error", "transient")))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([BuildMessage("m1"), BuildMessage("m2")]));

        var sut = CreateSut();
        await sut.PollNextAsync(); // seeds m1
        var failed = await sut.PollNextAsync(); // transient failure
        var recovered = await sut.PollNextAsync(); // m1 already seen, m2 new

        failed.IsFailure.Should().BeTrue();
        recovered.Value.Should().ContainSingle().Which.MessageId.Should().Be("m2");
    }

    // ── Dead-letter routing ──────────────────────────────────────────────────

    [Fact]
    public async Task PollNextAsync_FromDeadLetterTrue_CallsPeekDeadLetterMessagesAsync()
    {
        _messageOperationsMock
            .Setup(m => m.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([]));

        var sut = CreateSut(fromDeadLetter: true);

        await sut.PollNextAsync();

        _messageOperationsMock.Verify(
            m => m.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _messageOperationsMock.Verify(
            m => m.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task PollNextAsync_PassesRequestedEntityAndSubscription()
    {
        GetMessagesRequest? captured = null;
        _messageOperationsMock
            .Setup(m => m.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetMessagesRequest, CancellationToken>((req, _) => captured = req)
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([]));

        var sut = CreateSut(entityName: "orders-topic", subscriptionName: "orders-sub");

        await sut.PollNextAsync();

        captured.Should().NotBeNull();
        captured!.NamespaceId.Should().Be(NamespaceId);
        captured.EntityName.Should().Be("orders-topic");
        captured.SubscriptionName.Should().Be("orders-sub");
    }
}
