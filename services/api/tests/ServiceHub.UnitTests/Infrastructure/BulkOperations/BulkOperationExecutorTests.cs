using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
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
        _auditServiceMock.Object, NullLogger<BulkOperationExecutor>.Instance);

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
}
