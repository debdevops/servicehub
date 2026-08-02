using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.SignatureReplay;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.SignatureReplay;

public sealed class SignatureReplayExecutorTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<IMessageOperationsService> _messageOperationsMock = new();
    private readonly Guid _namespaceId = Guid.NewGuid();
    private const string OwnerId = "entra:test-owner-123";

    public SignatureReplayExecutorTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private SignatureReplayExecutor CreateSut() =>
        new(_dbContext, _messageOperationsMock.Object, NullLogger<SignatureReplayExecutor>.Instance);

    private DlqMessage AddDlqMessage(
        long seq = 1,
        DlqMessageStatus status = DlqMessageStatus.Active,
        string entityName = "orders")
    {
        var msg = new DlqMessage
        {
            MessageId = $"msg-{seq}",
            SequenceNumber = seq,
            BodyHash = $"hash-{seq}",
            NamespaceId = _namespaceId,
            OwnerId = OwnerId,
            EntityName = entityName,
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = status,
        };
        _dbContext.DlqMessages.Add(msg);
        _dbContext.SaveChanges();
        return msg;
    }

    private static SignatureReplayJobState CreateJob(
        IReadOnlyList<long> messageIds,
        DateTimeOffset? cancellationRequestedAt = null) => new()
    {
        Id = Guid.NewGuid(),
        OwnerId = OwnerId,
        NamespaceId = Guid.NewGuid(),
        NamespaceDisplayName = "ns",
        SignatureHash = "hash-1",
        MessageIds = messageIds,
        TotalMatched = messageIds.Count,
        CancellationRequestedAt = cancellationRequestedAt,
        CancellationTokenSource = new CancellationTokenSource(),
    };

    [Fact]
    public async Task ExecuteAsync_CancellationAlreadyRequested_MarksCancelledWithoutProcessing()
    {
        var sut = CreateSut();
        var message = AddDlqMessage();
        var job = CreateJob([message.Id], cancellationRequestedAt: DateTimeOffset.UtcNow);

        await sut.ExecuteAsync(job, job.CancellationTokenSource.Token);

        job.Status.Should().Be(BulkOperationStatus.Cancelled);
        job.ProcessedCount.Should().Be(0);
        _messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_SuccessfulReplay_UpdatesMessageAndJobProgress()
    {
        var sut = CreateSut();
        var message = AddDlqMessage(seq: 42);
        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(message.NamespaceId, "orders", null, 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var job = CreateJob([message.Id]);
        await sut.ExecuteAsync(job, job.CancellationTokenSource.Token);

        job.Status.Should().Be(BulkOperationStatus.Completed);
        job.ProcessedCount.Should().Be(1);
        job.SuccessCount.Should().Be(1);
        job.FailureCount.Should().Be(0);

        var stored = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        stored.Status.Should().Be(DlqMessageStatus.Replayed);
        stored.ReplaySuccess.Should().BeTrue();

        (await _dbContext.ReplayHistories.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ExecuteAsync_FailedReplay_MarksMessageReplayFailedAndJobCompletedWithErrors()
    {
        var sut = CreateSut();
        var message = AddDlqMessage(seq: 7);
        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(message.NamespaceId, "orders", null, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.ExternalService("Provider.Error", "boom")));

        var job = CreateJob([message.Id]);
        await sut.ExecuteAsync(job, job.CancellationTokenSource.Token);

        job.Status.Should().Be(BulkOperationStatus.CompletedWithErrors);
        job.FailureCount.Should().Be(1);
        job.FailureSample.Should().ContainSingle(f => f.Reason == "boom");

        var stored = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        stored.Status.Should().Be(DlqMessageStatus.ReplayFailed);
    }

    [Fact]
    public async Task ExecuteAsync_MessageNoLongerEligible_IsSkipped()
    {
        var sut = CreateSut();
        var message = AddDlqMessage(status: DlqMessageStatus.Replayed);

        var job = CreateJob([message.Id]);
        await sut.ExecuteAsync(job, job.CancellationTokenSource.Token);

        job.SkippedCount.Should().Be(1);
        job.SuccessCount.Should().Be(0);
        _messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CancelledMidBatch_StopsProcessingRemainingMessages()
    {
        var sut = CreateSut();
        var first = AddDlqMessage(seq: 1);
        var second = AddDlqMessage(seq: 2);

        var job = CreateJob([first.Id, second.Id]);

        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(first.NamespaceId, "orders", null, 1, It.IsAny<CancellationToken>()))
            .Callback(() => job.CancellationTokenSource.Cancel())
            .ReturnsAsync(Result.Success());

        await sut.ExecuteAsync(job, job.CancellationTokenSource.Token);

        job.Status.Should().Be(BulkOperationStatus.Cancelled);
        job.ProcessedCount.Should().Be(1);
        _messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(second.NamespaceId, "orders", null, 2, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_ReusesBulkOperationExecutorEntityResolution_ForSubscriptions()
    {
        var sut = CreateSut();
        var message = new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash-1",
            NamespaceId = _namespaceId,
            OwnerId = OwnerId,
            EntityName = "orders-topic/subscriptions/orders-sub",
            EntityType = ServiceBusEntityType.Subscription,
            TopicName = "orders-topic",
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        };
        _dbContext.DlqMessages.Add(message);
        await _dbContext.SaveChangesAsync();

        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders-topic", "orders-sub", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var job = CreateJob([message.Id]);
        await sut.ExecuteAsync(job, job.CancellationTokenSource.Token);

        job.SuccessCount.Should().Be(1);
        _messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(_namespaceId, "orders-topic", "orders-sub", 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
