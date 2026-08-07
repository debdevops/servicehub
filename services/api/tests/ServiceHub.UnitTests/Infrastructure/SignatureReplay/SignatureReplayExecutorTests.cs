using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
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

    private SignatureReplayJob CreateJob(
        IReadOnlyList<long> messageIds,
        DateTimeOffset? cancellationRequestedAt = null,
        BulkOperationStatus status = BulkOperationStatus.Pending)
    {
        var job = new SignatureReplayJob
        {
            Id = Guid.NewGuid(),
            OwnerId = OwnerId,
            Status = status,
            NamespaceId = Guid.NewGuid(),
            NamespaceDisplayName = "ns",
            SignatureHash = "hash-1",
            MessageIdsJson = JsonSerializer.Serialize(messageIds),
            TotalMatched = messageIds.Count,
            CreatedAt = DateTimeOffset.UtcNow,
            CancellationRequestedAt = cancellationRequestedAt,
        };
        _dbContext.SignatureReplayJobs.Add(job);
        _dbContext.SaveChanges();
        return job;
    }

    private async Task<SignatureReplayJob> ReloadAsync(Guid jobId) =>
        await _dbContext.SignatureReplayJobs.AsNoTracking().FirstAsync(j => j.Id == jobId);

    [Fact]
    public async Task ExecuteAsync_CancellationAlreadyRequested_MarksCancelledWithoutProcessing()
    {
        var sut = CreateSut();
        var message = AddDlqMessage();
        var job = CreateJob([message.Id], cancellationRequestedAt: DateTimeOffset.UtcNow);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var reloaded = await ReloadAsync(job.Id);
        reloaded.Status.Should().Be(BulkOperationStatus.Cancelled);
        reloaded.ProcessedCount.Should().Be(0);
        _messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_JobNotPending_SkipsDuplicateDequeue()
    {
        var sut = CreateSut();
        var message = AddDlqMessage();
        var job = CreateJob([message.Id], status: BulkOperationStatus.Running);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var reloaded = await ReloadAsync(job.Id);
        reloaded.Status.Should().Be(BulkOperationStatus.Running);
        _messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_UnknownJobId_LogsAndReturnsWithoutThrowing()
    {
        var sut = CreateSut();

        var act = async () => await sut.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().NotThrowAsync();
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
        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var reloaded = await ReloadAsync(job.Id);
        reloaded.Status.Should().Be(BulkOperationStatus.Completed);
        reloaded.ProcessedCount.Should().Be(1);
        reloaded.SuccessCount.Should().Be(1);
        reloaded.FailureCount.Should().Be(0);

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
        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var reloaded = await ReloadAsync(job.Id);
        reloaded.Status.Should().Be(BulkOperationStatus.CompletedWithErrors);
        reloaded.FailureCount.Should().Be(1);
        reloaded.FailureSampleJson.Should().Contain("boom");

        var stored = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        stored.Status.Should().Be(DlqMessageStatus.ReplayFailed);
    }

    [Fact]
    public async Task ExecuteAsync_MessageNoLongerEligible_IsSkipped()
    {
        var sut = CreateSut();
        var message = AddDlqMessage(status: DlqMessageStatus.Replayed);

        var job = CreateJob([message.Id]);
        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var reloaded = await ReloadAsync(job.Id);
        reloaded.SkippedCount.Should().Be(1);
        reloaded.SuccessCount.Should().Be(0);
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
        using var cts = new CancellationTokenSource();

        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(first.NamespaceId, "orders", null, 1, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(Result.Success());

        await sut.ExecuteAsync(job.Id, cts.Token);

        var reloaded = await ReloadAsync(job.Id);
        reloaded.Status.Should().Be(BulkOperationStatus.Cancelled);
        reloaded.ProcessedCount.Should().Be(1);
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
        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var reloaded = await ReloadAsync(job.Id);
        reloaded.SuccessCount.Should().Be(1);
        _messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(_namespaceId, "orders-topic", "orders-sub", 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Concurrency (C2) ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_TargetMessageClaimedByAnotherWorkerMidBatch_SkipsWithoutDuplicateReplay()
    {
        // Own shared-cache SQLite DB (not the class fixture's private ":memory:" one) so a
        // second, independent DlqDbContext — standing in for a concurrent bulk-replay or
        // auto-replay worker — can claim and complete the target message while this job's
        // own batch is still mid-flight. RunAsync snapshots its message list once up front
        // and processes it sequentially, so a message later in that snapshot can go stale
        // before this job reaches it — the actual C2 race.
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeperConnection = new SqliteConnection(connectionString);
        keeperConnection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options;
        using var dbContext = new DlqDbContext(options);
        dbContext.Database.EnsureCreated();

        // Ordered ahead of `target` by DetectedAtUtc, so RunAsync processes it first.
        var decoy = new DlqMessage
        {
            MessageId = "msg-decoy", SequenceNumber = 1, BodyHash = "hash-decoy",
            NamespaceId = _namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = DlqMessageStatus.Active,
        };
        var target = new DlqMessage
        {
            MessageId = "msg-target", SequenceNumber = 2, BodyHash = "hash-target",
            NamespaceId = _namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        };
        dbContext.DlqMessages.AddRange(decoy, target);
        await dbContext.SaveChangesAsync(); // assigns real, database-generated Ids

        var job = new SignatureReplayJob
        {
            Id = Guid.NewGuid(),
            OwnerId = OwnerId,
            Status = BulkOperationStatus.Pending,
            NamespaceId = _namespaceId,
            NamespaceDisplayName = "ns",
            SignatureHash = "hash-1",
            MessageIdsJson = JsonSerializer.Serialize(new List<long> { decoy.Id, target.Id }),
            TotalMatched = 2,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.SignatureReplayJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var messageOperationsMock = new Mock<IMessageOperationsService>();
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, decoy.SequenceNumber, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // While this job is still processing the decoy, a different worker (its own
                // context) claims and replays the target message underneath it.
                await using var racingContext = new DlqDbContext(options);
                var racingCopy = await racingContext.DlqMessages.SingleAsync(m => m.Id == target.Id);
                racingCopy.Status = DlqMessageStatus.Replayed;
                racingCopy.ReplayedAt = DateTimeOffset.UtcNow;
                racingCopy.ReplaySuccess = true;
                await racingContext.SaveChangesAsync();

                return Result.Success();
            });
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, target.SequenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var sut = new SignatureReplayExecutor(dbContext, messageOperationsMock.Object, NullLogger<SignatureReplayExecutor>.Instance);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        // This job's own claim attempt for `target` lost the race — it must never have
        // reached the provider a second time.
        messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(_namespaceId, "orders", null, target.SequenceNumber, It.IsAny<CancellationToken>()),
            Times.Never);

        var reloaded = await dbContext.SignatureReplayJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        reloaded.SuccessCount.Should().Be(1); // decoy
        reloaded.SkippedCount.Should().Be(1); // target — lost the claim race

        var storedTarget = await dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == target.Id);
        storedTarget.Status.Should().Be(DlqMessageStatus.Replayed); // the racing worker's write stands

        var targetHistoryCount = await dbContext.ReplayHistories.CountAsync(h => h.DlqMessageId == target.Id);
        targetHistoryCount.Should().Be(0); // this job never wrote history for target — it never got that far
    }
}
