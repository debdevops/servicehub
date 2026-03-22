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
        var result = await _service.GetHistoryAsync();
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

        var result = await _service.GetHistoryAsync(page: 1, pageSize: 3);
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

        var result = await _service.GetHistoryAsync(page: 2, pageSize: 3);
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

        var result = await _service.GetHistoryAsync(namespaceId: nsId);
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetHistory_FilterByEntityName()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1, entityName: "orders-queue"));
        _dbContext.DlqMessages.Add(CreateMessage(2, entityName: "payments-queue"));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetHistoryAsync(entityName: "orders");
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetHistory_FilterByStatus()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1, status: DlqMessageStatus.Active));
        _dbContext.DlqMessages.Add(CreateMessage(2, status: DlqMessageStatus.Replayed));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetHistoryAsync(status: DlqMessageStatus.Active);
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetHistory_FilterByCategory()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1, category: FailureCategory.Transient));
        _dbContext.DlqMessages.Add(CreateMessage(2, category: FailureCategory.Authorization));
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetHistoryAsync(category: FailureCategory.Transient);
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

        var result = await _service.GetHistoryAsync(from: now.AddHours(-1), to: now.AddHours(1));
        result.IsSuccess.Should().BeTrue();
    }

    // ── GetByIdAsync ────────────────────────────────────────

    [Fact]
    public async Task GetById_Exists_ReturnsMessage()
    {
        var msg = CreateMessage(1);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetByIdAsync(msg.Id);
        result.IsSuccess.Should().BeTrue();
        result.Value.MessageId.Should().Be("msg-1");
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsFailure()
    {
        var result = await _service.GetByIdAsync(999);
        result.IsFailure.Should().BeTrue();
    }

    // ── GetTimelineAsync ────────────────────────────────────

    [Fact]
    public async Task GetTimeline_BasicMessage_ReturnsEvents()
    {
        var msg = CreateMessage(1);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.GetTimelineAsync(msg.Id);
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

        var result = await _service.GetTimelineAsync(msg.Id);
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

        var result = await _service.GetTimelineAsync(msg.Id);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Contain(e => e.EventType == "Archived");
    }

    [Fact]
    public async Task GetTimeline_NotFound_ReturnsFailure()
    {
        var result = await _service.GetTimelineAsync(999);
        result.IsFailure.Should().BeTrue();
    }

    // ── UpdateNotesAsync ────────────────────────────────────

    [Fact]
    public async Task UpdateNotes_Exists_UpdatesNote()
    {
        var msg = CreateMessage(1);
        _dbContext.DlqMessages.Add(msg);
        await _dbContext.SaveChangesAsync();

        var result = await _service.UpdateNotesAsync(msg.Id, "Test note");
        result.IsSuccess.Should().BeTrue();
        result.Value.UserNotes.Should().Be("Test note");
    }

    [Fact]
    public async Task UpdateNotes_NotFound_ReturnsFailure()
    {
        var result = await _service.UpdateNotesAsync(999, "Note");
        result.IsFailure.Should().BeTrue();
    }

    // ── GetSummaryAsync ─────────────────────────────────────

    [Fact]
    public async Task GetSummary_Empty_ReturnsZeroCounts()
    {
        var result = await _service.GetSummaryAsync();
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

        var result = await _service.GetSummaryAsync();
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

        var result = await _service.GetSummaryAsync(namespaceId: nsId);
        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMessages.Should().Be(1);
    }

    // ── ExportAsync ─────────────────────────────────────────

    [Fact]
    public async Task Export_NoFilters_ReturnsAll()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1));
        _dbContext.DlqMessages.Add(CreateMessage(2));
        await _dbContext.SaveChangesAsync();

        var result = await _service.ExportAsync();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Export_FilterByStatus_ReturnsFiltered()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1, status: DlqMessageStatus.Active));
        _dbContext.DlqMessages.Add(CreateMessage(2, status: DlqMessageStatus.Replayed));
        await _dbContext.SaveChangesAsync();

        var result = await _service.ExportAsync(status: DlqMessageStatus.Active);
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
    }

    [Fact]
    public async Task Export_FilterByEntity_ReturnsFiltered()
    {
        _dbContext.DlqMessages.Add(CreateMessage(1, entityName: "orders"));
        _dbContext.DlqMessages.Add(CreateMessage(2, entityName: "payments"));
        await _dbContext.SaveChangesAsync();

        var result = await _service.ExportAsync(entityName: "orders");
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(1);
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
}
