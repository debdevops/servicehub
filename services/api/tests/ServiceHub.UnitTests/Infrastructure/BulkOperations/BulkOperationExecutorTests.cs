using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.BulkOperations;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BulkOperations;

public sealed class BulkOperationExecutorTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly Mock<IMessageOperationsService> _messageOperationsMock = new();
    private readonly Mock<IAuditService> _auditServiceMock = new();
    private readonly Mock<IPlatformEventBus> _eventBusMock = new();
    private readonly Guid _namespaceId = Guid.NewGuid();
    private const string OwnerId = "entra:test-owner-123";

    public BulkOperationExecutorTests()
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

    private BulkOperationExecutor CreateSut() => new(
        _dbContext, _namespaceRepositoryMock.Object, _messageOperationsMock.Object,
        _auditServiceMock.Object, _eventBusMock.Object, NullLogger<BulkOperationExecutor>.Instance);

    private Namespace SetupNamespace(EnvironmentType environment = EnvironmentType.Dev)
    {
        var ns = Namespace.Create("aws-ns", "akid:secret", environment: environment,
            provider: CloudProviderType.Aws, ownerId: OwnerId).Value;
        typeof(Namespace).GetProperty(nameof(Namespace.Id))!.SetValue(ns, _namespaceId);

        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        return ns;
    }

    private DlqMessage AddDlqMessage(
        long seq = 1,
        DlqMessageStatus status = DlqMessageStatus.Active,
        string entityName = "orders",
        ServiceBusEntityType entityType = ServiceBusEntityType.Queue,
        string? topicName = null)
    {
        var msg = new DlqMessage
        {
            MessageId = $"msg-{seq}",
            SequenceNumber = seq,
            BodyHash = $"hash-{seq}",
            NamespaceId = _namespaceId,
            OwnerId = OwnerId,
            EntityName = entityName,
            EntityType = entityType,
            TopicName = topicName,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = status,
        };
        _dbContext.DlqMessages.Add(msg);
        _dbContext.SaveChanges();
        return msg;
    }

    private async Task<BulkOperationJob> AddJobAsync(
        BulkOperationType operationType = BulkOperationType.Replay,
        BulkOperationStatus status = BulkOperationStatus.Pending,
        int totalMatched = 1,
        DateTimeOffset? cancellationRequestedAt = null,
        DlqMessageStatus? statusFilter = DlqMessageStatus.Active)
    {
        var job = new BulkOperationJob
        {
            OwnerId = OwnerId,
            OperationType = operationType,
            Status = status,
            NamespaceId = _namespaceId,
            NamespaceDisplayName = "aws-ns",
            StatusFilter = statusFilter,
            TotalMatched = totalMatched,
            CreatedAt = DateTimeOffset.UtcNow,
            CancellationRequestedAt = cancellationRequestedAt,
        };
        _dbContext.BulkOperationJobs.Add(job);
        await _dbContext.SaveChangesAsync();
        return job;
    }

    // ── Guard / lifecycle paths ──────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_JobNotFound_DoesNothing()
    {
        var sut = CreateSut();

        var act = async () => await sut.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ExecuteAsync_JobNotPending_SkipsProcessing()
    {
        var sut = CreateSut();
        var job = await AddJobAsync(status: BulkOperationStatus.Completed);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var stored = await _dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        stored.Status.Should().Be(BulkOperationStatus.Completed);
        _messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_CancellationAlreadyRequestedBeforeStart_MarksCancelledWithoutProcessing()
    {
        var sut = CreateSut();
        var job = await AddJobAsync(cancellationRequestedAt: DateTimeOffset.UtcNow);
        AddDlqMessage(1);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var stored = await _dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        stored.Status.Should().Be(BulkOperationStatus.Cancelled);
        stored.ProcessedCount.Should().Be(0);
    }

    [Fact]
    public async Task ExecuteAsync_NamespaceNoLongerExists_MarksJobFailed()
    {
        var sut = CreateSut();
        var job = await AddJobAsync();
        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("Namespace.NotFound", "gone")));

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var stored = await _dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        stored.Status.Should().Be(BulkOperationStatus.Failed);
        stored.ErrorSummary.Should().Contain("Namespace no longer exists");
    }

    [Fact]
    public async Task ExecuteAsync_NamespacePromotedToProdSinceCreation_MarksJobFailed()
    {
        var sut = CreateSut();
        SetupNamespace(EnvironmentType.Prod);
        var job = await AddJobAsync();

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var stored = await _dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        stored.Status.Should().Be(BulkOperationStatus.Failed);
        stored.ErrorSummary.Should().Contain("Production");
    }

    // ── Replay execution ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SuccessfulReplay_UpdatesMessageJobAndReplayHistory()
    {
        var sut = CreateSut();
        SetupNamespace();
        var message = AddDlqMessage(42);
        var job = await AddJobAsync();

        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var storedJob = await _dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        storedJob.Status.Should().Be(BulkOperationStatus.Completed);
        storedJob.ProcessedCount.Should().Be(1);
        storedJob.SuccessCount.Should().Be(1);
        storedJob.FailureCount.Should().Be(0);

        var storedMessage = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        storedMessage.Status.Should().Be(DlqMessageStatus.Replayed);
        storedMessage.ReplaySuccess.Should().BeTrue();

        var history = await _dbContext.ReplayHistories.AsNoTracking().FirstAsync(h => h.DlqMessageId == message.Id);
        history.OutcomeStatus.Should().Be("Success");
        history.ReplayedBy.Should().Be("bulk-operation");
    }

    [Fact]
    public async Task ExecuteAsync_FailedReplay_RecordsFailureAndSample()
    {
        var sut = CreateSut();
        SetupNamespace();
        var message = AddDlqMessage(7);
        var job = await AddJobAsync();

        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.ExternalService("Provider.Error", "message not found")));

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var storedJob = await _dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        storedJob.Status.Should().Be(BulkOperationStatus.CompletedWithErrors);
        storedJob.FailureCount.Should().Be(1);
        storedJob.FailureSampleJson.Should().Contain("message not found");

        var storedMessage = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        storedMessage.Status.Should().Be(DlqMessageStatus.ReplayFailed);
    }

    [Fact]
    public async Task ExecuteAsync_SubscriptionMessage_ResolvesTopicAndSubscriptionNames()
    {
        var sut = CreateSut();
        SetupNamespace();
        AddDlqMessage(1, entityName: "orders-topic/subscriptions/orders-sub",
            entityType: ServiceBusEntityType.Subscription, topicName: "orders-topic");
        var job = await AddJobAsync();

        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders-topic", "orders-sub", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        _messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(_namespaceId, "orders-topic", "orders-sub", 1, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_MessageAlreadyReplayedByAnotherPath_IsSkippedNotReplayedAgain()
    {
        var sut = CreateSut();
        SetupNamespace();
        AddDlqMessage(1, status: DlqMessageStatus.Replayed);
        // No status filter, so the match query still selects this message despite its current
        // status — exercising the executor's own defensive re-check (a status filter of
        // Active, the normal case, would have excluded it upstream instead).
        var job = await AddJobAsync(statusFilter: null);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var storedJob = await _dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        storedJob.SkippedCount.Should().Be(1);
        storedJob.SuccessCount.Should().Be(0);
        _messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<long>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── Purge execution ──────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SuccessfulPurge_MarksMessageDiscarded()
    {
        var sut = CreateSut();
        SetupNamespace();
        var message = AddDlqMessage(3);
        var job = await AddJobAsync(operationType: BulkOperationType.Purge);

        _messageOperationsMock
            .Setup(m => m.PurgeMessageAsync(_namespaceId, "orders", null, 3, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var storedMessage = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        storedMessage.Status.Should().Be(DlqMessageStatus.Discarded);

        var storedJob = await _dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        storedJob.SuccessCount.Should().Be(1);
    }

    // ── Purge eligibility (v3.6.0 P2-2) ──────────────────────────────────────
    //
    // The executor's eligibility re-check used to be Replay-only, while a purge job created
    // without a status filter matches every status. A purge could therefore overwrite a
    // successfully-Replayed message to Discarded (corrupting the replay audit narrative) and
    // report already-Discarded messages as provider failures.

    [Theory]
    [InlineData(DlqMessageStatus.Replayed)]
    [InlineData(DlqMessageStatus.Discarded)]
    [InlineData(DlqMessageStatus.Archived)]
    [InlineData(DlqMessageStatus.Resolved)]
    public async Task ExecuteAsync_Purge_SkipsIneligibleStatus_WithoutCallingProvider(
        DlqMessageStatus ineligibleStatus)
    {
        var sut = CreateSut();
        SetupNamespace();
        var message = AddDlqMessage(7, status: ineligibleStatus);
        var job = await AddJobAsync(operationType: BulkOperationType.Purge, statusFilter: null);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var storedJob = await _dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        storedJob.SkippedCount.Should().Be(1, "an ineligible message is skipped, not attempted");
        storedJob.FailureCount.Should().Be(0,
            "reporting a provider failure for a message that was never eligible is misleading");
        storedJob.SuccessCount.Should().Be(0);

        var storedMessage = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        storedMessage.Status.Should().Be(ineligibleStatus, "the original status must be preserved");

        _messageOperationsMock.Verify(
            m => m.PurgeMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ExecuteAsync_Purge_NeverOverwritesAReplayedMessageWithDiscarded()
    {
        var sut = CreateSut();
        SetupNamespace();
        var replayed = AddDlqMessage(8, status: DlqMessageStatus.Replayed);
        var job = await AddJobAsync(operationType: BulkOperationType.Purge, statusFilter: null);

        _messageOperationsMock
            .Setup(m => m.PurgeMessageAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string?>(),
                It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var stored = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == replayed.Id);
        stored.Status.Should().Be(DlqMessageStatus.Replayed,
            "overwriting Replayed with Discarded destroys the record that the replay succeeded");
    }

    [Fact]
    public async Task ExecuteAsync_Purge_ClaimsMessageBeforeCallingProvider()
    {
        // Status is an EF concurrency token; the claim is what stops a second concurrent job
        // issuing a duplicate provider delete. Observed by checking the persisted status at the
        // moment the provider call is made.
        var sut = CreateSut();
        SetupNamespace();
        var message = AddDlqMessage(9);
        var job = await AddJobAsync(operationType: BulkOperationType.Purge);

        DlqMessageStatus? statusWhenProviderCalled = null;
        _messageOperationsMock
            .Setup(m => m.PurgeMessageAsync(_namespaceId, "orders", null, 9, true, It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                using var probe = new DlqDbContext(
                    new DbContextOptionsBuilder<DlqDbContext>()
                        .UseSqlite(_dbContext.Database.GetDbConnection())
                        .Options);
                statusWhenProviderCalled = probe.DlqMessages.AsNoTracking()
                    .First(m => m.Id == message.Id).Status;
            })
            .ReturnsAsync(Result.Success());

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        statusWhenProviderCalled.Should().Be(DlqMessageStatus.Purging,
            "the claim must be committed before the provider is called, not after");

        var stored = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        stored.Status.Should().Be(DlqMessageStatus.Discarded);
    }

    [Fact]
    public async Task ExecuteAsync_Purge_ProviderFailure_ReleasesClaimSoRetryIsPossible()
    {
        var sut = CreateSut();
        SetupNamespace();
        var message = AddDlqMessage(10);
        var job = await AddJobAsync(operationType: BulkOperationType.Purge);

        _messageOperationsMock
            .Setup(m => m.PurgeMessageAsync(_namespaceId, "orders", null, 10, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.Internal("Purge.Failed", "provider exploded")));

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        var stored = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == message.Id);
        stored.Status.Should().Be(DlqMessageStatus.Active,
            "a failed purge must release its claim — leaving it Purging strands the message");

        var storedJob = await _dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        storedJob.FailureCount.Should().Be(1);
    }

    // ── Cancellation mid-batch ───────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CancelledMidBatch_StopsAndMarksCancelled_PartialProgressPersisted()
    {
        var sut = CreateSut();
        SetupNamespace();
        AddDlqMessage(1);
        AddDlqMessage(2);
        var job = await AddJobAsync(totalMatched: 2);

        using var cts = new CancellationTokenSource();

        // Cancel after the first message is processed, before the second.
        var callCount = 0;
        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, It.IsAny<long>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1) cts.Cancel();
                return Result.Success();
            });

        await sut.ExecuteAsync(job.Id, cts.Token);

        var storedJob = await _dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        storedJob.Status.Should().Be(BulkOperationStatus.Cancelled);
        storedJob.ProcessedCount.Should().Be(1);
        storedJob.SuccessCount.Should().Be(1);
    }

    // ── Audit ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OnCompletion_EnqueuesAContextFreeAuditEntry()
    {
        var sut = CreateSut();
        SetupNamespace();
        AddDlqMessage(1);
        var job = await AddJobAsync();

        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        _auditServiceMock.Verify(a => a.Enqueue(It.Is<AuditLog>(log =>
            log.OwnerId == OwnerId &&
            log.UserIdentity == "system:bulk-operation" &&
            log.Outcome == nameof(BulkOperationStatus.Completed))), Times.Once);
    }

    // ── Platform events ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_OnSuccessfulCompletion_PublishesBulkOperationCompletedEvent()
    {
        var sut = CreateSut();
        SetupNamespace();
        AddDlqMessage(1);
        var job = await AddJobAsync(operationType: BulkOperationType.Replay);

        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        _eventBusMock.Verify(b => b.PublishAsync(It.Is<PlatformEvent>(evt =>
            evt.EventType == EventTypes.BulkOperationCompleted &&
            evt.Category == EventCategories.BulkOperation &&
            evt.Severity == EventSeverity.Info &&
            evt.NamespaceId == _namespaceId &&
            evt.Actor == $"owner:{OwnerId}" &&
            GetPayload(evt).JobId == job.Id &&
            GetPayload(evt).OperationType == BulkOperationType.Replay &&
            GetPayload(evt).Status == BulkOperationStatus.Completed &&
            GetPayload(evt).SuccessCount == 1 &&
            GetPayload(evt).FailureCount == 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_OnFailedReplay_PublishesEventWithWarningSeverity()
    {
        var sut = CreateSut();
        SetupNamespace();
        AddDlqMessage(7);
        var job = await AddJobAsync();

        _messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(_namespaceId, "orders", null, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure(Error.ExternalService("Provider.Error", "message not found")));

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        _eventBusMock.Verify(b => b.PublishAsync(It.Is<PlatformEvent>(evt =>
            evt.Severity == EventSeverity.Warning &&
            GetPayload(evt).Status == BulkOperationStatus.CompletedWithErrors &&
            GetPayload(evt).FailureCount == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_JobNotPending_DoesNotPublishEvent()
    {
        var sut = CreateSut();
        var job = await AddJobAsync(status: BulkOperationStatus.Completed);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        _eventBusMock.Verify(b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static BulkOperationCompletedPayload GetPayload(PlatformEvent evt) =>
        (BulkOperationCompletedPayload)evt.Payload!;

    // ── Concurrency (C2) ─────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_TargetMessageClaimedByAnotherWorkerMidBatch_SkipsWithoutDuplicateReplay()
    {
        // Own shared-cache SQLite DB (not the class fixture's private ":memory:" one) so a
        // second, independent DlqDbContext — standing in for a concurrent signature-replay
        // or auto-replay worker — can claim and complete the target message while this job's
        // own batch is still mid-flight, reproducing the actual C2 race: RunAsync snapshots
        // its eligible-message list once up front, then processes it sequentially, so a
        // message near the back of that snapshot can go stale before this job reaches it.
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeperConnection = new SqliteConnection(connectionString);
        keeperConnection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options;
        using var dbContext = new DlqDbContext(options);
        dbContext.Database.EnsureCreated();

        var namespaceId = Guid.NewGuid();
        var ns = Namespace.Create("aws-ns", "akid:secret", environment: EnvironmentType.Dev,
            provider: CloudProviderType.Aws, ownerId: OwnerId).Value;
        typeof(Namespace).GetProperty(nameof(Namespace.Id))!.SetValue(ns, namespaceId);
        var namespaceRepositoryMock = new Mock<INamespaceRepository>();
        namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        // Ordered ahead of `target` by DetectedAtUtc, so RunAsync processes it first.
        var decoy = new DlqMessage
        {
            MessageId = "msg-decoy", SequenceNumber = 1, BodyHash = "hash-decoy",
            NamespaceId = namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = DlqMessageStatus.Active,
        };
        var target = new DlqMessage
        {
            MessageId = "msg-target", SequenceNumber = 2, BodyHash = "hash-target",
            NamespaceId = namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        };
        dbContext.DlqMessages.AddRange(decoy, target);

        var job = new BulkOperationJob
        {
            OwnerId = OwnerId,
            OperationType = BulkOperationType.Replay,
            Status = BulkOperationStatus.Pending,
            NamespaceId = namespaceId,
            NamespaceDisplayName = "aws-ns",
            StatusFilter = DlqMessageStatus.Active,
            TotalMatched = 2,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.BulkOperationJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var messageOperationsMock = new Mock<IMessageOperationsService>();
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(namespaceId, "orders", null, decoy.SequenceNumber, It.IsAny<CancellationToken>()))
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
            .Setup(m => m.ReplayMessageAsync(namespaceId, "orders", null, target.SequenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());

        var sut = new BulkOperationExecutor(
            dbContext, namespaceRepositoryMock.Object, messageOperationsMock.Object,
            Mock.Of<IAuditService>(), Mock.Of<IPlatformEventBus>(), NullLogger<BulkOperationExecutor>.Instance);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        // This job's own claim attempt for `target` lost the race — it must never have
        // reached the provider a second time.
        messageOperationsMock.Verify(
            m => m.ReplayMessageAsync(namespaceId, "orders", null, target.SequenceNumber, It.IsAny<CancellationToken>()),
            Times.Never);

        var storedJob = await dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        storedJob.SuccessCount.Should().Be(1); // decoy
        storedJob.SkippedCount.Should().Be(1); // target — lost the claim race

        var storedTarget = await dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == target.Id);
        storedTarget.Status.Should().Be(DlqMessageStatus.Replayed); // the racing worker's write stands

        var targetHistoryCount = await dbContext.ReplayHistories.CountAsync(h => h.DlqMessageId == target.Id);
        targetHistoryCount.Should().Be(0); // this job never wrote history for target — it never got that far
    }

    [Fact]
    public async Task ExecuteAsync_CancelledMidCallWhileAnotherWriterMovedTheInFlightMessage_StillPersistsTerminalState()
    {
        // Reproduces the live bug behind jobs stuck reporting Running forever. The claim step
        // (`message.Status = Replaying; SaveChangesAsync(cancellationToken)`) is the one save in
        // this whole flow guarded only by the job's own cancellable token — if cancellation
        // fires mid-write, that save throws OperationCanceledException (not
        // DbUpdateConcurrencyException, so ProcessMessageAsync's own concurrency handling never
        // runs) and leaves the message entity Modified with an now-stale original Status. This
        // test fabricates exactly that post-condition directly via EF's tracking API (real
        // interleaving is a narrow, timing-dependent race — see the live incident this
        // regresses), then confirms the finally block's cleanup save survives a conflicting
        // write from another writer (e.g. DlqMonitorWorker's reconciliation) on that same stale
        // entity instead of throwing DbUpdateConcurrencyException uncaught and silently
        // abandoning the job (swallowed by BulkOperationWorker's outer per-job catch, leaving
        // the DB row at Status=Running forever with no further writer ever touching it again).
        var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
        using var keeperConnection = new SqliteConnection(connectionString);
        keeperConnection.Open();

        var options = new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options;
        using var dbContext = new DlqDbContext(options);
        dbContext.Database.EnsureCreated();

        var namespaceId = Guid.NewGuid();
        var ns = Namespace.Create("aws-ns", "akid:secret", environment: EnvironmentType.Dev,
            provider: CloudProviderType.Aws, ownerId: OwnerId).Value;
        typeof(Namespace).GetProperty(nameof(Namespace.Id))!.SetValue(ns, namespaceId);
        var namespaceRepositoryMock = new Mock<INamespaceRepository>();
        namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var target = new DlqMessage
        {
            MessageId = "msg-target", SequenceNumber = 1, BodyHash = "hash-target",
            NamespaceId = namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        };
        dbContext.DlqMessages.Add(target);

        var job = new BulkOperationJob
        {
            OwnerId = OwnerId,
            OperationType = BulkOperationType.Replay,
            Status = BulkOperationStatus.Pending,
            NamespaceId = namespaceId,
            NamespaceDisplayName = "aws-ns",
            StatusFilter = DlqMessageStatus.Active,
            TotalMatched = 1,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.BulkOperationJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var messageOperationsMock = new Mock<IMessageOperationsService>();
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(namespaceId, "orders", null, target.SequenceNumber, It.IsAny<CancellationToken>()))
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

        var sut = new BulkOperationExecutor(
            dbContext, namespaceRepositoryMock.Object, messageOperationsMock.Object,
            Mock.Of<IAuditService>(), Mock.Of<IPlatformEventBus>(), NullLogger<BulkOperationExecutor>.Instance);

        var act = async () => await sut.ExecuteAsync(job.Id, CancellationToken.None);
        await act.Should().NotThrowAsync();

        var storedJob = await dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        storedJob.Status.Should().Be(BulkOperationStatus.Cancelled);
        storedJob.CompletedAt.Should().NotBeNull();

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

        var namespaceId = Guid.NewGuid();
        var ns = Namespace.Create("aws-ns", "akid:secret", environment: EnvironmentType.Dev,
            provider: CloudProviderType.Aws, ownerId: OwnerId).Value;
        typeof(Namespace).GetProperty(nameof(Namespace.Id))!.SetValue(ns, namespaceId);
        var namespaceRepositoryMock = new Mock<INamespaceRepository>();
        namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var first = new DlqMessage
        {
            MessageId = "msg-first", SequenceNumber = 1, BodyHash = "hash-first",
            NamespaceId = namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = DlqMessageStatus.Active,
        };
        var second = new DlqMessage
        {
            MessageId = "msg-second", SequenceNumber = 2, BodyHash = "hash-second",
            NamespaceId = namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        };
        dbContext.DlqMessages.AddRange(first, second);

        var job = new BulkOperationJob
        {
            OwnerId = OwnerId,
            OperationType = BulkOperationType.Replay,
            Status = BulkOperationStatus.Pending,
            NamespaceId = namespaceId,
            NamespaceDisplayName = "aws-ns",
            StatusFilter = DlqMessageStatus.Active,
            TotalMatched = 2,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.BulkOperationJobs.Add(job);
        await dbContext.SaveChangesAsync();

        var messageOperationsMock = new Mock<IMessageOperationsService>();
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(namespaceId, "orders", null, first.SequenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(namespaceId, "orders", null, second.SequenceNumber, It.IsAny<CancellationToken>()))
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

        var sut = new BulkOperationExecutor(
            dbContext, namespaceRepositoryMock.Object, messageOperationsMock.Object,
            Mock.Of<IAuditService>(), Mock.Of<IPlatformEventBus>(), NullLogger<BulkOperationExecutor>.Instance);

        var act = async () => await sut.ExecuteAsync(job.Id, CancellationToken.None);
        await act.Should().NotThrowAsync();

        var storedJob = await dbContext.BulkOperationJobs.AsNoTracking().FirstAsync(j => j.Id == job.Id);
        storedJob.Status.Should().Be(BulkOperationStatus.Completed);
        storedJob.ProcessedCount.Should().Be(2);
        storedJob.SuccessCount.Should().Be(2); // both messages actually succeeded — the batch was not aborted
        storedJob.FailureCount.Should().Be(0);
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

        var namespaceId = Guid.NewGuid();
        var ns = Namespace.Create("aws-ns", "akid:secret", environment: EnvironmentType.Dev,
            provider: CloudProviderType.Aws, ownerId: OwnerId).Value;
        typeof(Namespace).GetProperty(nameof(Namespace.Id))!.SetValue(ns, namespaceId);
        var namespaceRepositoryMock = new Mock<INamespaceRepository>();
        namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var first = new DlqMessage
        {
            MessageId = "msg-1", SequenceNumber = 1, BodyHash = "hash-1",
            NamespaceId = namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-1),
            Status = DlqMessageStatus.Active,
        };
        var second = new DlqMessage
        {
            MessageId = "msg-2", SequenceNumber = 2, BodyHash = "hash-2",
            NamespaceId = namespaceId, OwnerId = OwnerId, EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        };
        dbContext.DlqMessages.AddRange(first, second);

        var job = new BulkOperationJob
        {
            OwnerId = OwnerId,
            OperationType = BulkOperationType.Replay,
            Status = BulkOperationStatus.Pending,
            NamespaceId = namespaceId,
            NamespaceDisplayName = "aws-ns",
            StatusFilter = DlqMessageStatus.Active,
            TotalMatched = 2,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        dbContext.BulkOperationJobs.Add(job);
        await dbContext.SaveChangesAsync();

        DlqMessageStatus? firstMessageStatusObservedDuringSecondMessage = null;

        var messageOperationsMock = new Mock<IMessageOperationsService>();
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(namespaceId, "orders", null, first.SequenceNumber, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success());
        messageOperationsMock
            .Setup(m => m.ReplayMessageAsync(namespaceId, "orders", null, second.SequenceNumber, It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                await using var observerContext = new DlqDbContext(options);
                var observedFirst = await observerContext.DlqMessages.AsNoTracking().SingleAsync(m => m.Id == first.Id);
                firstMessageStatusObservedDuringSecondMessage = observedFirst.Status;
                return Result.Success();
            });

        var sut = new BulkOperationExecutor(
            dbContext, namespaceRepositoryMock.Object, messageOperationsMock.Object,
            Mock.Of<IAuditService>(), Mock.Of<IPlatformEventBus>(), NullLogger<BulkOperationExecutor>.Instance);

        await sut.ExecuteAsync(job.Id, CancellationToken.None);

        firstMessageStatusObservedDuringSecondMessage.Should().Be(DlqMessageStatus.Replayed);
    }
}
