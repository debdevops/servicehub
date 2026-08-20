using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure;

public class DlqHistoryServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<ILogger<DlqHistoryService>> _logger = new();
    private readonly DlqHistoryService _service;

    public DlqHistoryServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new DlqHistoryService(_dbContext, _logger.Object);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private DlqMessage CreateMessage(
        long seq = 1,
        DlqMessageStatus status = DlqMessageStatus.Active,
        FailureCategory category = FailureCategory.Transient,
        Guid? namespaceId = null,
        string entityName = "test-queue")
    {
        return new DlqMessage
        {
            MessageId = $"msg-{seq}",
            SequenceNumber = seq,
            BodyHash = $"hash-{seq}",
            NamespaceId = namespaceId ?? Guid.NewGuid(),
            OwnerId = TestConstants.TestOwnerId,
            EntityName = entityName,
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-2),
            DeadLetterTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = "MaxDeliveryCountExceeded",
            DeadLetterErrorDescription = "Max delivery count reached",
            DeliveryCount = 10,
            ContentType = "application/json",
            MessageSize = 256,
            BodyPreview = "{ \"test\": true }",
            FailureCategory = category,
            CategoryConfidence = 0.95,
            Status = status
        };
    }

    // ── Constructor ─────────────────────────────────────────

    [Fact]
    public void Constructor_NullDbContext_Throws()
    {
        var act = () => new DlqHistoryService(null!, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new DlqHistoryService(_dbContext, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── GetHistoryAsync ─────────────────────────────────────

    [Fact]
    public async Task GetHistory_NoMessages_ReturnsEmptyPage()
    {
        var result = await _service.GetHistoryAsync(TestConstants.TestOwnerId);
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetHistory_WithMessages_ReturnsPaginated()
    {
        for (int i = 1; i <= 5; i++)
            _dbContext.DlqMessages.Add(CreateMessage(i));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetHistoryAsync(TestConstants.TestOwnerId, page: 1, pageSize: 3);
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(3);
        result.Value.TotalCount.Should().Be(5);
        result.Value.HasNextPage.Should().BeTrue();
        result.Value.HasPreviousPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetHistory_SecondPage_HasPreviousPage()
    {
        for (int i = 1; i <= 5; i++)
            _dbContext.DlqMessages.Add(CreateMessage(i));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetHistoryAsync(TestConstants.TestOwnerId, page: 2, pageSize: 3);
        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.HasPreviousPage.Should().BeTrue();
        result.Value.HasNextPage.Should().BeFalse();
    }

    [Fact]
    public async Task GetHistory_FilterByNamespace()
    {
        var nsId = Guid.NewGuid();
        _dbContext.DlqMessages.Add(CreateMessage(1, namespaceId: nsId));
        _dbContext.DlqMessages.Add(CreateMessage(2, namespaceId: Guid.NewGuid()));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetHistoryAsync(TestConstants.TestOwnerId, namespaceId: nsId);
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetHistory_FilterByEntityName()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1, entityName: "orders-queue"));
        _dbContext.DlqMessages.Add(CreateMessage(2, entityName: "payments-queue"));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetHistoryAsync(TestConstants.TestOwnerId, entityName: "orders");
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetHistory_FilterByStatus()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1, status: DlqMessageStatus.Active));
        _dbContext.DlqMessages.Add(CreateMessage(2, status: DlqMessageStatus.Replayed));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetHistoryAsync(TestConstants.TestOwnerId, status: DlqMessageStatus.Active);
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetHistory_FilterByCategory()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1, category: FailureCategory.Transient));
        _dbContext.DlqMessages.Add(CreateMessage(2, category: FailureCategory.Authorization));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetHistoryAsync(TestConstants.TestOwnerId, category: FailureCategory.Transient);
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetHistory_FilterByDateRange()
    {
        var now = DateTimeOffset.UtcNow;
        var msg1 = CreateMessage(1);
        var msg2 = CreateMessage(2);
        _dbContext.DlqMessages.Add(msg1);
        _dbContext.DlqMessages.Add(msg2);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetHistoryAsync(TestConstants.TestOwnerId, from: now.AddHours(-1), to: now.AddHours(1));
        result.IsSuccess.Should().BeTrue();
    }

    // ── GetByIdAsync ────────────────────────────────────────

    [Fact]
    public async Task GetById_Exists_ReturnsMessage()
    {
        var msg = CreateMessage(1);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetByIdAsync(TestConstants.TestOwnerId, msg.Id);
        result.IsSuccess.Should().BeTrue();
        result.Value.MessageId.Should().Be("msg-1");
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsFailure()
    {
        var result = await _service.GetByIdAsync(TestConstants.TestOwnerId, 999);
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetById_DifferentOwner_ReturnsFailure()
    {
        var msg = CreateMessage(1);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetByIdAsync(TestConstants.AltOwnerId, msg.Id);
        result.IsFailure.Should().BeTrue();
    }

    // ── GetTimelineAsync ────────────────────────────────────

    [Fact]
    public async Task GetTimeline_BasicMessage_ReturnsEvents()
    {
        var msg = CreateMessage(1);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetTimelineAsync(TestConstants.TestOwnerId, msg.Id);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(e => e.EventType == "Enqueued");
        result.Value.Should().Contain(e => e.EventType == "DeadLettered");
        result.Value.Should().Contain(e => e.EventType == "Detected");
    }

    [Fact]
    public async Task GetTimeline_WithReplayHistory_IncludesReplayEvents()
    {
        var msg = CreateMessage(1);
        msg.ReplayedAt = DateTimeOffset.UtcNow;
        msg.Status = DlqMessageStatus.Replayed;
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        _dbContext.ReplayHistories.Add(new ReplayHistory
        {
            DlqMessageId = msg.Id,
            ReplayedAt = DateTimeOffset.UtcNow,
            ReplayedBy = "test-user",
            ReplayStrategy = "original-entity",
            ReplayedToEntity = "test-queue",
            OutcomeStatus = "Success"
        });
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetTimelineAsync(TestConstants.TestOwnerId, msg.Id);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(e => e.EventType == "ReplayedSuccess");
        result.Value.Should().Contain(e => e.EventType == "StatusChanged");
    }

    [Fact]
    public async Task GetTimeline_ArchivedMessage_IncludesArchivedEvent()
    {
        var msg = CreateMessage(1);
        msg.ArchivedAt = DateTimeOffset.UtcNow;
        msg.Status = DlqMessageStatus.Archived;
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetTimelineAsync(TestConstants.TestOwnerId, msg.Id);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(e => e.EventType == "Archived");
    }

    [Fact]
    public async Task GetTimeline_NotFound_ReturnsFailure()
    {
        var result = await _service.GetTimelineAsync(TestConstants.TestOwnerId, 999);
        result.IsFailure.Should().BeTrue();
    }

    // ── UpdateNotesAsync ────────────────────────────────────

    [Fact]
    public async Task UpdateNotes_Exists_UpdatesNote()
    {
        var msg = CreateMessage(1);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateNotesAsync(TestConstants.TestOwnerId, msg.Id, "Test note");
        result.IsSuccess.Should().BeTrue();
        result.Value.UserNotes.Should().Be("Test note");
    }

    [Fact]
    public async Task UpdateNotes_NotFound_ReturnsFailure()
    {
        var result = await _service.UpdateNotesAsync(TestConstants.TestOwnerId, 999, "Note");
        result.IsFailure.Should().BeTrue();
    }

    // ── GetSummaryAsync ─────────────────────────────────────

    [Fact]
    public async Task GetSummary_Empty_ReturnsZeroCounts()
    {
        var result = await _service.GetSummaryAsync(TestConstants.TestOwnerId);
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMessages.Should().Be(0);
        result.Value.ActiveMessages.Should().Be(0);
    }

    [Fact]
    public async Task GetSummary_WithMessages_ReturnsCorrectCounts()
    {
        var nsId = Guid.NewGuid();
        _dbContext.DlqMessages.Add(CreateMessage(1, status: DlqMessageStatus.Active, category: FailureCategory.Transient, namespaceId: nsId));
        _dbContext.DlqMessages.Add(CreateMessage(2, status: DlqMessageStatus.Active, category: FailureCategory.MaxDelivery, namespaceId: nsId));
        _dbContext.DlqMessages.Add(CreateMessage(3, status: DlqMessageStatus.Replayed, namespaceId: nsId));
        _dbContext.DlqMessages.Add(CreateMessage(4, status: DlqMessageStatus.Archived, namespaceId: nsId));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetSummaryAsync(TestConstants.TestOwnerId);
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMessages.Should().Be(4);
        result.Value.ActiveMessages.Should().Be(2);
        result.Value.ReplayedMessages.Should().Be(1);
        result.Value.ArchivedMessages.Should().Be(1);
        result.Value.ByCategory.Should().ContainKey("Transient");
        result.Value.ByCategory.Should().ContainKey("MaxDelivery");
    }

    [Fact]
    public async Task GetSummary_FilterByNamespace()
    {
        var nsId = Guid.NewGuid();
        _dbContext.DlqMessages.Add(CreateMessage(1, namespaceId: nsId));
        _dbContext.DlqMessages.Add(CreateMessage(2, namespaceId: Guid.NewGuid()));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetSummaryAsync(TestConstants.TestOwnerId, namespaceId: nsId);
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMessages.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_WithDaysParameter_FiltersTrendByDate()
    {
        var nsId = Guid.NewGuid();

        // Recent message — within 7 days
        var recentMsg = CreateMessage(1, namespaceId: nsId);  // DetectedAtUtc = UtcNow by default
        _dbContext.DlqMessages.Add(recentMsg);

        // Old message — 60 days ago (outside the trend window, but still counted in totals)
        var oldMsg = new DlqMessage
        {
            MessageId = "msg-old",
            SequenceNumber = 99,
            BodyHash = "hash-old",
            NamespaceId = nsId,
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddDays(-61),
            DeadLetterTimeUtc = DateTimeOffset.UtcNow.AddDays(-60),
            DetectedAtUtc = DateTimeOffset.UtcNow.AddDays(-60),
            DeadLetterReason = "MaxDeliveryCountExceeded",
            DeadLetterErrorDescription = "Max delivery count reached",
            DeliveryCount = 10,
            ContentType = "application/json",
            MessageSize = 256,
            BodyPreview = "{ \"test\": true }",
            FailureCategory = FailureCategory.Transient,
            CategoryConfidence = 0.95,
            Status = DlqMessageStatus.Active
        };
        _dbContext.DlqMessages.Add(oldMsg);

        await _dbContext.SaveChangesAsync();

        // TotalMessages always counts all messages regardless of days
        var result7 = await _service.GetSummaryAsync(TestConstants.TestOwnerId, namespaceId: nsId, days: 7);
        result7.IsSuccess.Should().BeTrue();
        result7.Value.TotalMessages.Should().Be(2);
        // Only today's message appears in the 7-day trend
        result7.Value.DailyTrend.Sum(t => t.NewMessages).Should().Be(1);

        // With 365-day window, both messages appear in trend
        var result365 = await _service.GetSummaryAsync(TestConstants.TestOwnerId, namespaceId: nsId, days: 365);
        result365.IsSuccess.Should().BeTrue();
        result365.Value.TotalMessages.Should().Be(2);
        result365.Value.DailyTrend.Sum(t => t.NewMessages).Should().Be(2);
    }

    [Fact]
    public async Task GetSummary_TrendIsZeroFilledForEveryDayInWindow()
    {
        var nsId = Guid.NewGuid();

        // Only two days have activity within the 7-day window, several days apart —
        // the trend must still be a continuous daily series, not just the days with data.
        var today = new DlqMessage
        {
            MessageId = "msg-today",
            SequenceNumber = 1,
            BodyHash = "hash-today",
            NamespaceId = nsId,
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-2),
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 10,
            MessageSize = 256,
            FailureCategory = FailureCategory.Transient,
            Status = DlqMessageStatus.Active
        };
        var fourDaysAgo = new DlqMessage
        {
            MessageId = "msg-old",
            SequenceNumber = 2,
            BodyHash = "hash-old",
            NamespaceId = nsId,
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddDays(-4),
            DetectedAtUtc = DateTimeOffset.UtcNow.AddDays(-4),
            DeliveryCount = 10,
            MessageSize = 256,
            FailureCategory = FailureCategory.Transient,
            Status = DlqMessageStatus.Active
        };
        _dbContext.DlqMessages.AddRange(today, fourDaysAgo);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetSummaryAsync(TestConstants.TestOwnerId, namespaceId: nsId, days: 7);

        result.IsSuccess.Should().BeTrue();
        result.Value.DailyTrend.Should().HaveCount(7);
        result.Value.DailyTrend.Sum(t => t.NewMessages).Should().Be(2);
        result.Value.DailyTrend.Count(t => t.NewMessages == 0).Should().Be(5);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(500)]
    public async Task GetSummary_DaysClampedToValidRange_DoesNotThrow(int inputDays)
    {
        // Just verify it doesn't throw — the clamping is internal
        var result = await _service.GetSummaryAsync(TestConstants.TestOwnerId, days: inputDays);
        result.IsSuccess.Should().BeTrue();
    }

    // ── ExportAsync ─────────────────────────────────────────

    [Fact]
    public async Task Export_NoFilters_ReturnsAll()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1));
        _dbContext.DlqMessages.Add(CreateMessage(2));
        await _dbContext.SaveChangesAsync();

        var result = await _service.ExportAsync(TestConstants.TestOwnerId);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Export_FilterByStatus_ReturnsFiltered()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1, status: DlqMessageStatus.Active));
        _dbContext.DlqMessages.Add(CreateMessage(2, status: DlqMessageStatus.Replayed));
        await _dbContext.SaveChangesAsync();

        var result = await _service.ExportAsync(TestConstants.TestOwnerId, status: DlqMessageStatus.Active);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Export_FilterByEntity_ReturnsFiltered()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1, entityName: "orders"));
        _dbContext.DlqMessages.Add(CreateMessage(2, entityName: "payments"));
        await _dbContext.SaveChangesAsync();

        var result = await _service.ExportAsync(TestConstants.TestOwnerId, entityName: "orders");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Export_DifferentOwner_ExcludesOtherTenantMessages()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1));
        _dbContext.DlqMessages.Add(CreateMessage(2));
        await _dbContext.SaveChangesAsync();

        var result = await _service.ExportAsync(TestConstants.AltOwnerId);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSummary_DifferentOwner_ExcludesOtherTenantMessages()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1));
        _dbContext.DlqMessages.Add(CreateMessage(2));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetSummaryAsync(TestConstants.AltOwnerId);
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMessages.Should().Be(0);
    }

    // ── UpdateForensicResultAsync ───────────────────────────

    [Fact]
    public async Task UpdateForensicResult_Exists_Updates()
    {
        var msg = CreateMessage(1);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateForensicResultAsync(
            msg.Id, FailureCategory.MaxDelivery, 0.95, "Max delivery exceeded", "RequiresReview");
        result.IsSuccess.Should().BeTrue();
        result.Value.FailureCategory.Should().Be(FailureCategory.MaxDelivery);
        result.Value.ForensicRootCause.Should().Be("Max delivery exceeded");
        result.Value.ReplaySafety.Should().Be("RequiresReview");
    }

    [Fact]
    public async Task UpdateForensicResult_NotFound_ReturnsFailure()
    {
        var result = await _service.UpdateForensicResultAsync(
            999, FailureCategory.Transient, 0.5, "Root cause", "Safe");
        result.IsFailure.Should().BeTrue();
    }

    // ── LookupAsync ─────────────────────────────────────────

    [Fact]
    public async Task LookupAsync_ExistingMessage_ReturnsMessage()
    {
        var nsId = Guid.NewGuid();
        var msg = CreateMessage(seq: 42, namespaceId: nsId, entityName: "my-queue");
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.LookupAsync(nsId, "my-queue", 42);

        result.IsSuccess.Should().BeTrue();
        result.Value.SequenceNumber.Should().Be(42);
        result.Value.NamespaceId.Should().Be(nsId);
    }

    [Fact]
    public async Task LookupAsync_NotFound_ReturnsFailure()
    {
        var result = await _service.LookupAsync(Guid.NewGuid(), "no-such-queue", 99999);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Dlq.NotFound");
    }

    // ── UpdateStatusAsync (triage) ──────────────────────────

    [Theory]
    [InlineData(DlqMessageStatus.Archived)]
    [InlineData(DlqMessageStatus.Resolved)]
    public async Task UpdateStatusAsync_ValidTarget_TransitionsAndStampsTimestamps(DlqMessageStatus target)
    {
        var msg = CreateMessage(seq: 1, status: DlqMessageStatus.Active);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateStatusAsync(TestConstants.TestOwnerId, msg.Id, target);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(target);
        if (target == DlqMessageStatus.Archived)
            result.Value.ArchivedAt.Should().NotBeNull();
        else
            result.Value.ResolvedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_ResolvedTarget_RecordsDeclaredByOperatorCause()
    {
        var msg = CreateMessage(seq: 1, status: DlqMessageStatus.Active);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateStatusAsync(TestConstants.TestOwnerId, msg.Id, DlqMessageStatus.Resolved);

        result.IsSuccess.Should().BeTrue();
        result.Value.ResolutionCause.Should().Be(DlqResolutionCause.DeclaredByOperator);
    }

    [Fact]
    public async Task UpdateStatusAsync_DiscardedTarget_IsRejected()
    {
        // Discarded must mean "ServiceHub destroyed this via a provider call" (P8) — manual
        // triage has no such call to point to, so it can no longer declare Discarded by hand.
        var msg = CreateMessage(seq: 1, status: DlqMessageStatus.Active);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateStatusAsync(TestConstants.TestOwnerId, msg.Id, DlqMessageStatus.Discarded);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Dlq.InvalidStatusTransition");
    }

    [Fact]
    public async Task UpdateStatusAsync_ReopenToActive_ClearsResolutionStamps()
    {
        var msg = CreateMessage(seq: 2, status: DlqMessageStatus.Archived);
        msg.ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateStatusAsync(TestConstants.TestOwnerId, msg.Id, DlqMessageStatus.Active);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(DlqMessageStatus.Active);
        result.Value.ArchivedAt.Should().BeNull();
        result.Value.ResolvedAt.Should().BeNull();
    }

    [Fact]
    public async Task UpdateStatusAsync_AppendsNotes_WhenProvided()
    {
        var msg = CreateMessage(seq: 3, status: DlqMessageStatus.Active);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateStatusAsync(
            TestConstants.TestOwnerId, msg.Id, DlqMessageStatus.Resolved, "fixed upstream");

        result.IsSuccess.Should().BeTrue();
        result.Value.UserNotes.Should().Be("fixed upstream");
    }

    [Fact]
    public async Task UpdateStatusAsync_AppendsNotes_ToExistingNotes()
    {
        var msg = CreateMessage(seq: 4, status: DlqMessageStatus.Active);
        msg.UserNotes = "investigating";
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateStatusAsync(
            TestConstants.TestOwnerId, msg.Id, DlqMessageStatus.Resolved, "fixed upstream");

        result.IsSuccess.Should().BeTrue();
        result.Value.UserNotes.Should().Be($"investigating{Environment.NewLine}fixed upstream");
    }

    [Fact]
    public async Task UpdateStatusAsync_ArchivedToResolved_ClearsArchivedAt()
    {
        var msg = CreateMessage(seq: 5, status: DlqMessageStatus.Archived);
        msg.ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateStatusAsync(
            TestConstants.TestOwnerId, msg.Id, DlqMessageStatus.Resolved);

        result.IsSuccess.Should().BeTrue();
        result.Value.ArchivedAt.Should().BeNull();
        result.Value.ResolvedAt.Should().NotBeNull();
    }

    [Theory]
    [InlineData(DlqMessageStatus.Replayed)]
    [InlineData(DlqMessageStatus.ReplayFailed)]
    public async Task UpdateStatusAsync_ReplayOutcomeTarget_IsRejected(DlqMessageStatus target)
    {
        var msg = CreateMessage(seq: 4, status: DlqMessageStatus.Active);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateStatusAsync(TestConstants.TestOwnerId, msg.Id, target);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Dlq.InvalidStatusTransition");
    }

    [Fact]
    public async Task UpdateStatusAsync_WrongOwner_ReturnsNotFound()
    {
        var msg = CreateMessage(seq: 5, status: DlqMessageStatus.Active);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateStatusAsync("someone-else", msg.Id, DlqMessageStatus.Resolved);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Dlq.NotFound");
    }

    // ── FinalizeRecoveryAsync ────────────────────────────────

    [Fact]
    public async Task FinalizeRecoveryAsync_ReplaySuccess_TransitionsToReplayedAndStampsOutcome()
    {
        var msg = CreateMessage(seq: 1, status: DlqMessageStatus.Replaying);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, TestConstants.TestOwnerId, DlqMessageStatus.Replaying, DlqMessageStatus.Replayed);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(DlqMessageStatus.Replayed);
        result.Value.ReplayedAt.Should().NotBeNull();
        result.Value.ReplaySuccess.Should().BeTrue();
    }

    [Fact]
    public async Task FinalizeRecoveryAsync_ReplayFailure_TransitionsToReplayFailed()
    {
        var msg = CreateMessage(seq: 2, status: DlqMessageStatus.Replaying);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, TestConstants.TestOwnerId, DlqMessageStatus.Replaying, DlqMessageStatus.ReplayFailed);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(DlqMessageStatus.ReplayFailed);
        result.Value.ReplaySuccess.Should().BeFalse();
    }

    [Fact]
    public async Task FinalizeRecoveryAsync_PurgeSuccess_TransitionsToDiscarded()
    {
        var msg = CreateMessage(seq: 3, status: DlqMessageStatus.Purging);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, TestConstants.TestOwnerId, DlqMessageStatus.Purging, DlqMessageStatus.Discarded);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(DlqMessageStatus.Discarded);
    }

    [Fact]
    public async Task FinalizeRecoveryAsync_PurgeFailure_ReleasesClaimBackToActive()
    {
        var msg = CreateMessage(seq: 4, status: DlqMessageStatus.Purging);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, TestConstants.TestOwnerId, DlqMessageStatus.Purging, DlqMessageStatus.Active);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(DlqMessageStatus.Active);
    }

    [Fact]
    public async Task FinalizeRecoveryAsync_MessageNotInExpectedClaimStatus_ReturnsConflictWithoutMutating()
    {
        // The message never held the Replaying claim (e.g. a duplicate finalize call, or one that
        // lost a race) — must not be allowed to finalize a claim it doesn't own.
        var msg = CreateMessage(seq: 5, status: DlqMessageStatus.Active);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, TestConstants.TestOwnerId, DlqMessageStatus.Replaying, DlqMessageStatus.Replayed);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Dlq.ClaimNotHeld");

        var reloaded = await _dbContext.DlqMessages.AsNoTracking().FirstAsync(m => m.Id == msg.Id);
        reloaded.Status.Should().Be(DlqMessageStatus.Active);
    }

    [Fact]
    public async Task FinalizeRecoveryAsync_UnknownId_ReturnsNotFound()
    {
        var result = await _service.FinalizeRecoveryAsync(
            999999, TestConstants.TestOwnerId, DlqMessageStatus.Replaying, DlqMessageStatus.Replayed);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Dlq.NotFound");
    }

    [Fact]
    public async Task FinalizeRecoveryAsync_WrongOwner_ReturnsNotFound()
    {
        var msg = CreateMessage(seq: 6, status: DlqMessageStatus.Replaying);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, "someone-else", DlqMessageStatus.Replaying, DlqMessageStatus.Replayed);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Dlq.NotFound");
    }

    // ── FinalizeRecoveryAsync + ReplayHistory ────────────────

    [Fact]
    public async Task FinalizeRecoveryAsync_ReplaySuccess_WithReplayHistory_CreatesExactlyOneRowMatchingConventions()
    {
        var msg = CreateMessage(seq: 7, status: DlqMessageStatus.Replaying);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var details = new ReplayHistoryDetails(
            ReplayedBy: "ApiKey:test-key",
            ReplayStrategy: "original-entity",
            ReplayedToEntity: "test-queue");

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, TestConstants.TestOwnerId, DlqMessageStatus.Replaying, DlqMessageStatus.Replayed, details);

        result.IsSuccess.Should().BeTrue();

        var histories = await _dbContext.ReplayHistories.Where(h => h.DlqMessageId == msg.Id).ToListAsync();
        histories.Should().HaveCount(1);
        var history = histories[0];
        history.RuleId.Should().BeNull();
        history.ReplayedBy.Should().Be("ApiKey:test-key");
        history.ReplayStrategy.Should().Be("original-entity");
        history.ReplayedToEntity.Should().Be("test-queue");
        history.OutcomeStatus.Should().Be("Success");
        history.ErrorDetails.Should().BeNull();
        history.ReplayedAt.Should().Be(result.Value.ReplayedAt!.Value);
    }

    [Fact]
    public async Task FinalizeRecoveryAsync_ReplayFailure_WithReplayHistory_CreatesFailedOutcomeRow()
    {
        var msg = CreateMessage(seq: 8, status: DlqMessageStatus.Replaying);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var details = new ReplayHistoryDetails(
            ReplayedBy: "ApiKey:test-key",
            ReplayStrategy: "original-entity",
            ReplayedToEntity: "test-queue",
            ErrorDetails: "SQS error");

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, TestConstants.TestOwnerId, DlqMessageStatus.Replaying, DlqMessageStatus.ReplayFailed, details);

        result.IsSuccess.Should().BeTrue();

        var histories = await _dbContext.ReplayHistories.Where(h => h.DlqMessageId == msg.Id).ToListAsync();
        histories.Should().HaveCount(1);
        histories[0].OutcomeStatus.Should().Be("Failed");
        histories[0].ErrorDetails.Should().Be("SQS error");
    }

    [Fact]
    public async Task FinalizeRecoveryAsync_ClaimConflict_WithReplayHistory_CreatesNoRow()
    {
        // Message never held the Replaying claim — finalize fails before any write, including
        // the ReplayHistory row, even though the caller supplied details.
        var msg = CreateMessage(seq: 9, status: DlqMessageStatus.Active);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var details = new ReplayHistoryDetails("ApiKey:test-key", "original-entity", "test-queue");

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, TestConstants.TestOwnerId, DlqMessageStatus.Replaying, DlqMessageStatus.Replayed, details);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Dlq.ClaimNotHeld");

        var histories = await _dbContext.ReplayHistories.Where(h => h.DlqMessageId == msg.Id).ToListAsync();
        histories.Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeRecoveryAsync_PurgeSuccess_ReplayHistoryProvidedDefensively_CreatesNoRow()
    {
        // Purge's terminal status (Discarded) never produces a ReplayHistory row, even if a
        // caller mistakenly passed details — the guard is on terminalStatus, not just the null
        // check, matching every other purge path in this codebase (no ReplayHistory for purges).
        var msg = CreateMessage(seq: 10, status: DlqMessageStatus.Purging);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var details = new ReplayHistoryDetails("ApiKey:test-key", "original-entity", "test-queue");

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, TestConstants.TestOwnerId, DlqMessageStatus.Purging, DlqMessageStatus.Discarded, details);

        result.IsSuccess.Should().BeTrue();

        var histories = await _dbContext.ReplayHistories.Where(h => h.DlqMessageId == msg.Id).ToListAsync();
        histories.Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeRecoveryAsync_PurgeFailure_ReplayHistoryProvidedDefensively_CreatesNoRow()
    {
        var msg = CreateMessage(seq: 11, status: DlqMessageStatus.Purging);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var details = new ReplayHistoryDetails("ApiKey:test-key", "original-entity", "test-queue");

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, TestConstants.TestOwnerId, DlqMessageStatus.Purging, DlqMessageStatus.Active, details);

        result.IsSuccess.Should().BeTrue();

        var histories = await _dbContext.ReplayHistories.Where(h => h.DlqMessageId == msg.Id).ToListAsync();
        histories.Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeRecoveryAsync_ReplaySuccess_NoReplayHistoryArgument_CreatesNoRow()
    {
        // Backward-compatibility: callers that don't pass replayHistory (default null) get the
        // pre-fix behavior — status/timestamps update, no ReplayHistory row.
        var msg = CreateMessage(seq: 12, status: DlqMessageStatus.Replaying);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.FinalizeRecoveryAsync(
            msg.Id, TestConstants.TestOwnerId, DlqMessageStatus.Replaying, DlqMessageStatus.Replayed);

        result.IsSuccess.Should().BeTrue();

        var histories = await _dbContext.ReplayHistories.Where(h => h.DlqMessageId == msg.Id).ToListAsync();
        histories.Should().BeEmpty();
    }
}
