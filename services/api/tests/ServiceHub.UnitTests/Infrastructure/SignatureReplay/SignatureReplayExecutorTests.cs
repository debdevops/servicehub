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
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.SignatureReplay;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.SignatureReplay;

public sealed class SignatureReplayExecutorTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<IMessageOperationsService> _messageOperationsMock = new();
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
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

        SetupNamespace(); // default: Dev, so existing behavioral tests aren't blocked
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private SignatureReplayExecutor CreateSut() =>
        new(_dbContext, _namespaceRepositoryMock.Object, _messageOperationsMock.Object,
            new RecoveryLedgerService(_dbContext), NullLogger<SignatureReplayExecutor>.Instance);

    private Namespace SetupNamespace(Guid? namespaceId = null, EnvironmentType environment = EnvironmentType.Dev)
    {
        var ns = Namespace.Create("azure-ns", "Endpoint=sb://test/;SharedAccessKey=key", environment: environment,
            provider: CloudProviderType.Azure, ownerId: OwnerId).Value;
        var id = namespaceId ?? _namespaceId;
        typeof(Namespace).GetProperty(nameof(Namespace.Id))!.SetValue(ns, id);
        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));
        return ns;
    }

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
            NamespaceId = _namespaceId,
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

    // ── Production re-check at execution time (H1) ──────────────────────────

    [Fact]
    public async Task ExecuteAsync_NamespacePromotedToProdSinceCreation_MarksJobFailedWithoutReplaying()
    {
        var sut = CreateSut();
        SetupNamespace(environment: EnvironmentType.Prod);
        var message = AddDlqMessage();
        var job = CreateJob([message.Id]);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var reloaded = await ReloadAsync(job.Id);
        reloaded.Status.Should().Be(BulkOperationStatus.Failed);
        reloaded.ErrorSummary.Should().Contain("Production");
        reloaded.ProcessedCount.Should().Be(0);
        _messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_NamespaceNoLongerExists_MarksJobFailedWithoutReplaying()
    {
        var sut = CreateSut();
        var message = AddDlqMessage();
        var job = CreateJob([message.Id]);
        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("Namespace.NotFound", "not found")));

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var reloaded = await ReloadAsync(job.Id);
        reloaded.Status.Should().Be(BulkOperationStatus.Failed);
        reloaded.ErrorSummary.Should().Contain("Namespace no longer exists");
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

        var sut = new SignatureReplayExecutor(dbContext, _namespaceRepositoryMock.Object, messageOperationsMock.Object, new RecoveryLedgerService(dbContext), NullLogger<SignatureReplayExecutor>.Instance);

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

    [Fact]
    public async Task ExecuteAsync_CancelledMidCallWhileAnotherWriterMovedTheInFlightMessage_StillPersistsTerminalState()
    {
        // Reproduces the live bug behind jobs stuck reporting Running forever. The claim step
        // (`message.Status = Replaying; SaveChangesAsync(cancellationToken)`) is the only save
        // in this whole flow guarded solely by the job's own cancellable token — if
        // cancellation fires mid-write, that save throws OperationCanceledException (not
        // DbUpdateConcurrencyException, so ProcessMessageAsync's own concurrency handling for
        // the claim never runs), leaving the claimed message tracked as dirty. If another
        // writer (e.g. DlqMonitorWorker's reconciliation) has since moved that same row on, the
        // finally block's own cleanup save — the only save left to persist the job's terminal
        // status — hits that same conflict. This test forces the entity dirty via EF's tracking
        // API rather than racing the real, narrow, timing-dependent window, then confirms the
        // cleanup save survives the conflict instead of throwing DbUpdateConcurrencyException
        // uncaught and silently abandoning the job (swallowed by SignatureReplayWorker's outer
        // per-job catch, leaving the DB row at Status=Running forever with no further writer
        // ever touching it again).
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeperConnection = new SqliteConnection(connectionString);
        keeperConnection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options;
        using var dbContext = new DlqDbContext(options);
        dbContext.Database.EnsureCreated();

        var target = new DlqMessage
        {
            MessageId = "msg-target", SequenceNumber = 1, BodyHash = "hash-target",
            NamespaceId = _namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        };
        dbContext.DlqMessages.Add(target);
        await dbContext.SaveChangesAsync(); // assigns a real, database-generated Id

        var job = new SignatureReplayJob
        {
            Id = Guid.NewGuid(),
            OwnerId = OwnerId,
            Status = BulkOperationStatus.Pending,
            NamespaceId = _namespaceId,
            NamespaceDisplayName = "ns",
            SignatureHash = "hash-1",
            MessageIdsJson = JsonSerializer.Serialize(new List<long> { target.Id }),
            TotalMatched = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.SignatureReplayJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var messageOperationsMock = new Mock<IMessageOperationsService>();
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, target.SequenceNumber, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // By this point the real claim save has already committed (it runs before
                // ReplayMessageAsync is invoked), so the tracked entity is Unchanged. Force it
                // dirty again to fabricate the postcondition a claim save interrupted by
                // cancellation mid-write leaves behind — real interleaving is a narrow,
                // timing-dependent race between that save and the user's Cancel click, not
                // reproducible deterministically here.
                dbContext.Entry(target).State = EntityState.Modified;

                // Meanwhile another writer (e.g. DlqMonitorWorker's reconciliation) moves the
                // same row on, so the entry's now-stale original value no longer matches it.
                await using var racingContext = new DlqDbContext(options);
                var racingCopy = await racingContext.DlqMessages.SingleAsync(m => m.Id == target.Id);
                racingCopy.Status = DlqMessageStatus.Replayed;
                racingCopy.ReplayedAt = DateTimeOffset.UtcNow;
                racingCopy.ReplaySuccess = true;
                await racingContext.SaveChangesAsync();

                // ...then cancellation fires mid-call, before this job's own outcome save (the
                // one place that already knows how to reconcile a conflict) ever runs.
                throw new OperationCanceledException();
            });

        var sut = new SignatureReplayExecutor(dbContext, _namespaceRepositoryMock.Object, messageOperationsMock.Object, new RecoveryLedgerService(dbContext), NullLogger<SignatureReplayExecutor>.Instance);

        var act = async () => await sut.ExecuteAsync(job.Id, CancellationToken.None);
        await act.Should().NotThrowAsync();

        var reloaded = await dbContext.SignatureReplayJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        reloaded.Status.Should().Be(BulkOperationStatus.Cancelled);
        reloaded.CompletedAt.Should().NotBeNull();

        var storedTarget = await dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == target.Id);
        storedTarget.Status.Should().Be(DlqMessageStatus.Replayed); // the racing writer's status stands
    }

    [Fact]
    public async Task ExecuteAsync_StaleEarlierMessageConflictsAtCheckpointSave_DoesNotAbortRestOfBatch()
    {
        // Regresses a variant of the same bug hit live: a second message's checkpoint save
        // (the one right after ProcessMessageAsync returns, inside RunAsync's loop — the exact
        // save that failed against real DEV DLQ traffic while validating the finally-block fix
        // above) can find an *earlier*, already-processed message in the batch left dirty and
        // stale by an unrelated concurrent writer. Before this fix, that aborted the whole
        // remaining batch (job ends Failed after processing only part of TotalMatched) even
        // though every individual message actually succeeded — violating ProcessMessageAsync's
        // own documented contract that a losing save "must not fail the message outcome or
        // abort the rest of the batch" (see its outcome-save comment). The checkpoint save must
        // absorb the same class of conflict the finally-block save does.
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeperConnection = new SqliteConnection(connectionString);
        keeperConnection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options;
        using var dbContext = new DlqDbContext(options);
        dbContext.Database.EnsureCreated();

        var first = new DlqMessage
        {
            MessageId = "msg-first", SequenceNumber = 1, BodyHash = "hash-first",
            NamespaceId = _namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = DlqMessageStatus.Active,
        };
        var second = new DlqMessage
        {
            MessageId = "msg-second", SequenceNumber = 2, BodyHash = "hash-second",
            NamespaceId = _namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        };
        dbContext.DlqMessages.AddRange(first, second);
        await dbContext.SaveChangesAsync(); // assigns real, database-generated Ids

        var job = new SignatureReplayJob
        {
            Id = Guid.NewGuid(),
            OwnerId = OwnerId,
            Status = BulkOperationStatus.Pending,
            NamespaceId = _namespaceId,
            NamespaceDisplayName = "ns",
            SignatureHash = "hash-1",
            MessageIdsJson = JsonSerializer.Serialize(new List<long> { first.Id, second.Id }),
            TotalMatched = 2,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.SignatureReplayJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var messageOperationsMock = new Mock<IMessageOperationsService>();
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, first.SequenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, second.SequenceNumber, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                // `first` already finished processing cleanly (its own outcome save committed,
                // so it's tracked Unchanged). Fabricate it going stale again — standing in for
                // a concurrent writer (e.g. DlqMonitorWorker) touching it mid-batch — so that
                // `second`'s upcoming checkpoint save, which flushes the whole change tracker
                // and so still includes `first`, hits the same conflict class as the
                // finally-block fix above, but for an unrelated earlier message this time.
                dbContext.Entry(first).State = EntityState.Modified;

                await using var racingContext = new DlqDbContext(options);
                var racingCopy = await racingContext.DlqMessages.SingleAsync(m => m.Id == first.Id);
                racingCopy.Status = DlqMessageStatus.Replayed;
                racingCopy.ReplayedAt = DateTimeOffset.UtcNow;
                racingCopy.ReplaySuccess = true;
                await racingContext.SaveChangesAsync();

                return Result.Success();
            });

        var sut = new SignatureReplayExecutor(dbContext, _namespaceRepositoryMock.Object, messageOperationsMock.Object, new RecoveryLedgerService(dbContext), NullLogger<SignatureReplayExecutor>.Instance);

        var act = async () => await sut.ExecuteAsync(job.Id, CancellationToken.None);
        await act.Should().NotThrowAsync();

        var reloaded = await dbContext.SignatureReplayJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        reloaded.Status.Should().Be(BulkOperationStatus.Completed);
        reloaded.ProcessedCount.Should().Be(2);
        reloaded.SuccessCount.Should().Be(2); // both messages actually succeeded — the batch was not aborted
        reloaded.FailureCount.Should().Be(0);
    }

    // ── Crash-window checkpointing (H2) ─────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_EachMessageOutcomeIsDurablyPersistedBeforeTheNextMessageIsProcessed()
    {
        // Own shared-cache SQLite DB so a second, independent DlqDbContext can observe
        // committed state mid-batch — standing in for "what a crash right now would leave
        // behind". Regresses H2: progress used to be checkpointed only every 5 messages, so a
        // crash between checkpoints could leave an already-replayed message's outcome
        // unpersisted. If that were still true here, the first message's status would still
        // read back as `Replaying` (its pre-outcome claim) instead of `Replayed` while the
        // second message is being processed.
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeperConnection = new SqliteConnection(connectionString);
        keeperConnection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options;
        using var dbContext = new DlqDbContext(options);
        dbContext.Database.EnsureCreated();

        var first = new DlqMessage
        {
            MessageId = "msg-1", SequenceNumber = 1, BodyHash = "hash-1",
            NamespaceId = _namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = DlqMessageStatus.Active,
        };
        var second = new DlqMessage
        {
            MessageId = "msg-2", SequenceNumber = 2, BodyHash = "hash-2",
            NamespaceId = _namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        };
        dbContext.DlqMessages.AddRange(first, second);
        await dbContext.SaveChangesAsync(); // assigns real, database-generated Ids

        var job = new SignatureReplayJob
        {
            Id = Guid.NewGuid(),
            OwnerId = OwnerId,
            Status = BulkOperationStatus.Pending,
            NamespaceId = _namespaceId,
            NamespaceDisplayName = "ns",
            SignatureHash = "hash-1",
            MessageIdsJson = JsonSerializer.Serialize(new List<long> { first.Id, second.Id }),
            TotalMatched = 2,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.SignatureReplayJobs.Add(job);
        await dbContext.SaveChangesAsync();

        DlqMessageStatus? firstMessageStatusObservedDuringSecondMessage = null;

        var messageOperationsMock = new Mock<IMessageOperationsService>();
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, first.SequenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, second.SequenceNumber, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await using var observerContext = new DlqDbContext(options);
                var observedFirst = await observerContext.DlqMessages.AsNoTracking().SingleAsync(m => m.Id == first.Id);
                firstMessageStatusObservedDuringSecondMessage = observedFirst.Status;
                return Result.Success();
            });

        var sut = new SignatureReplayExecutor(dbContext, _namespaceRepositoryMock.Object, messageOperationsMock.Object, new RecoveryLedgerService(dbContext), NullLogger<SignatureReplayExecutor>.Instance);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        firstMessageStatusObservedDuringSecondMessage.Should().Be(DlqMessageStatus.Replayed);
    }
}
