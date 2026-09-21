using FluentAssertions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.LiveTail;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.LiveTail;

public sealed class LiveTailSessionTests
{
    private readonly Mock<IMessageOperationsService> _messageOperationsMock = new();
    private static readonly Guid NamespaceId = Guid.NewGuid();

    // GCP by default for the provider-neutral behaviour below (single-snapshot-per-poll,
    // MessageId dedup, no sequence cursor) — unchanged from this class's original design.
    // Azure-specific sequential-catch-up behaviour is covered separately further down.
    private LiveTailSession CreateSut(
        string entityName = "orders",
        string? subscriptionName = null,
        bool fromDeadLetter = false,
        CloudProviderType provider = CloudProviderType.Gcp) =>
        new(_messageOperationsMock.Object, NamespaceId, entityName, subscriptionName, fromDeadLetter, provider);

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
        var act = () => new LiveTailSession(null!, NamespaceId, "orders", null, false, CloudProviderType.Gcp);
        act.Should().Throw<ArgumentNullException>().WithParameterName("messageOperationsService");
    }

    [Fact]
    public void Constructor_NullEntityName_Throws()
    {
        var act = () => new LiveTailSession(_messageOperationsMock.Object, NamespaceId, null!, null, false, CloudProviderType.Gcp);
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

    // ── Azure sequential catch-up (regression: a backlog larger than one poll batch used
    //    to permanently strand Live Tail on page 1 — see LiveTailSession's remarks) ────────

    [Fact]
    public async Task PollNextAsync_Azure_BacklogLargerThanOneBatch_CatchesUpWithinOnePollAndSeesLaterArrivals()
    {
        // A fresh Azure receiver is created (and its position lost) on every real Peek call,
        // so the mock must behave the same way the real provider does: honour whatever
        // FromSequenceNumber was requested rather than silently continuing where the last
        // call left off, which is exactly the assumption that let the real bug ship unnoticed.
        var backlog = Enumerable.Range(1, 30).Select(i => BuildMessage($"backlog-{i}", i)).ToList();

        _messageOperationsMock
            .Setup(m => m.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GetMessagesRequest req, CancellationToken _) =>
            {
                var from = req.FromSequenceNumber ?? 1;
                var page = backlog.Where(m => m.SequenceNumber >= from).Take(req.MaxMessages).ToList();
                return Result<IReadOnlyList<Message>>.Success(page);
            });

        var sut = CreateSut(provider: CloudProviderType.Azure);

        // One poll call must internally page through the entire 30-message backlog (2 batches
        // of 25 + a short one) without emitting any of it — the whole point of "first poll
        // seeds without emitting" must hold regardless of backlog size.
        var first = await sut.PollNextAsync();
        first.Value.Should().BeEmpty();

        // A message that arrives *after* the backlog (sequence 31) must be visible on the very
        // next poll — this is the exact scenario that was broken: with no cursor, every poll
        // re-peeked the same oldest 25 (sequence 1-25) forever and never saw sequence 31.
        backlog.Add(BuildMessage("new-arrival", 31));
        var second = await sut.PollNextAsync();

        second.Value.Should().ContainSingle().Which.MessageId.Should().Be("new-arrival");
    }

    [Fact]
    public async Task PollNextAsync_Azure_PassesAdvancingFromSequenceNumber()
    {
        var requests = new List<GetMessagesRequest>();
        _messageOperationsMock
            .Setup(m => m.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetMessagesRequest, CancellationToken>((req, _) => requests.Add(req))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([BuildMessage("m1", 5)]));

        var sut = CreateSut(provider: CloudProviderType.Azure);
        await sut.PollNextAsync();
        await sut.PollNextAsync();

        requests.Should().HaveCount(2);
        requests[0].FromSequenceNumber.Should().BeNull();
        requests[1].FromSequenceNumber.Should().Be(6); // one past the highest sequence number seen
    }

    [Fact]
    public async Task PollNextAsync_Gcp_NeverPassesFromSequenceNumber()
    {
        // GCP's SequenceNumber rotates per redelivery — a cursor built from it would be
        // meaningless, so GCP must keep relying purely on MessageId dedup, unaffected by
        // this fix.
        var requests = new List<GetMessagesRequest>();
        _messageOperationsMock
            .Setup(m => m.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .Callback<GetMessagesRequest, CancellationToken>((req, _) => requests.Add(req))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success([BuildMessage("m1", 5), BuildMessage("m2", 999)]));

        var sut = CreateSut(provider: CloudProviderType.Gcp);
        await sut.PollNextAsync();
        await sut.PollNextAsync();

        requests.Should().HaveCount(2).And.OnlyContain(r => r.FromSequenceNumber == null);
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
