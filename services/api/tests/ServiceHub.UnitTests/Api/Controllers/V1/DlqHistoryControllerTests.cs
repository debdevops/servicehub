using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class DlqHistoryControllerTests
{
    private readonly Mock<IDlqHistoryService> _historyService = new();
    private readonly Mock<IForensicEngine> _forensicEngine = new();
    private readonly Mock<ILogger<DlqHistoryController>> _logger = new();
    private readonly DlqHistoryController _controller;

    public DlqHistoryControllerTests()
    {
        _controller = new DlqHistoryController(
            _historyService.Object,
            _forensicEngine.Object,
            _logger.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    private static DlqMessage CreateTestMessage(long id = 1)
    {
        return new DlqMessage
        {
            MessageId = $"msg-{id}",
            SequenceNumber = id,
            BodyHash = $"hash-{id}",
            NamespaceId = Guid.NewGuid(),
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-1),
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeadLetterReason = "MaxDeliveryCountExceeded",
            DeadLetterErrorDescription = "Max delivery count reached",
            DeliveryCount = 10,
            ContentType = "application/json",
            MessageSize = 256,
            BodyPreview = "{ \"test\": true }",
            FailureCategory = FailureCategory.MaxDelivery,
            CategoryConfidence = 0.95,
            Status = DlqMessageStatus.Active,
            ForensicRootCause = "Max delivery exceeded",
            ForensicConfidence = 0.9,
            ReplaySafety = "RequiresReview",
            ReplayHistories = new List<ReplayHistory>()
        };
    }

    // ── Constructor ─────────────────────────────────────────

    [Fact]
    public void Constructor_NullHistoryService_Throws()
    {
        var act = () => new DlqHistoryController(null!, _forensicEngine.Object, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("historyService");
    }

    [Fact]
    public void Constructor_NullForensicEngine_Throws()
    {
        var act = () => new DlqHistoryController(_historyService.Object, null!, _logger.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("forensicEngine");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new DlqHistoryController(_historyService.Object, _forensicEngine.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── GetHistory ──────────────────────────────────────────

    [Fact]
    public async Task GetHistory_Success_ReturnsPaginatedResponse()
    {
        var messages = new List<DlqMessage> { CreateTestMessage(1), CreateTestMessage(2) };
        var pageResult = new DlqHistoryPageResult(
            Items: messages, TotalCount: 2, Page: 1, PageSize: 50,
            HasNextPage: false, HasPreviousPage: false);

        _historyService.Setup(s => s.GetHistoryAsync(
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<DlqMessageStatus?>(),
            It.IsAny<FailureCategory?>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqHistoryPageResult>.Success(pageResult));

        var result = await _controller.GetHistory();
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<PaginatedResponse<DlqHistoryResponse>>().Subject;
        response.Items.Should().HaveCount(2);
        response.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetHistory_Failure_ReturnsError()
    {
        _historyService.Setup(s => s.GetHistoryAsync(
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<DlqMessageStatus?>(),
            It.IsAny<FailureCategory?>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqHistoryPageResult>.Failure(Error.Internal("DB_ERROR", "Database error")));

        var result = await _controller.GetHistory();
        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetHistory_ClampsPageSize()
    {
        var pageResult = new DlqHistoryPageResult(
            Items: new List<DlqMessage>(), TotalCount: 0, Page: 1, PageSize: 200,
            HasNextPage: false, HasPreviousPage: false);

        _historyService.Setup(s => s.GetHistoryAsync(
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<DlqMessageStatus?>(),
            It.IsAny<FailureCategory?>(), It.IsAny<int>(), 200,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqHistoryPageResult>.Success(pageResult));

        var result = await _controller.GetHistory(pageSize: 999);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().NotBeNull();
    }

    // ── GetById ─────────────────────────────────────────────

    [Fact]
    public async Task GetById_Success_ReturnsDetail()
    {
        var msg = CreateTestMessage(42);
        _historyService.Setup(s => s.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqMessage>.Success(msg));

        var result = await _controller.GetById(42);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<DlqMessageDetailResponse>().Subject;
        response.MessageId.Should().Be("msg-42");
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsError()
    {
        _historyService.Setup(s => s.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqMessage>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.GetById(99);
        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    // ── GetTimeline ─────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_Success_ReturnsEvents()
    {
        var events = new List<DlqTimelineEvent>
        {
            new("Detected", "Message detected in DLQ", DateTimeOffset.UtcNow),
            new("Analysed", "Forensic engine analysed", DateTimeOffset.UtcNow)
        };

        _historyService.Setup(s => s.GetTimelineAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DlqTimelineEvent>>.Success(events));

        var result = await _controller.GetTimeline(1);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<DlqTimelineResponse>().Subject;
        response.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTimeline_Failure_ReturnsError()
    {
        _historyService.Setup(s => s.GetTimelineAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DlqTimelineEvent>>.Failure(
                Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.GetTimeline(99);
        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    // ── UpdateNotes ─────────────────────────────────────────

    [Fact]
    public async Task UpdateNotes_Success_ReturnsUpdatedMessage()
    {
        var msg = CreateTestMessage(1);
        msg.UserNotes = "Updated note";

        _historyService.Setup(s => s.UpdateNotesAsync(1, "Updated note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqMessage>.Success(msg));

        var request = new UpdateDlqNotesRequest("Updated note");
        var result = await _controller.UpdateNotes(1, request);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<DlqHistoryResponse>().Subject;
        response.UserNotes.Should().Be("Updated note");
    }

    [Fact]
    public async Task UpdateNotes_NotFound_ReturnsError()
    {
        _historyService.Setup(s => s.UpdateNotesAsync(99, "note", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqMessage>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.UpdateNotes(99, new UpdateDlqNotesRequest("note"));
        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    // ── Export ───────────────────────────────────────────────

    [Fact]
    public async Task Export_Json_ReturnsJsonFile()
    {
        var messages = new List<DlqMessage> { CreateTestMessage(1) };

        _historyService.Setup(s => s.ExportAsync(
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<DlqMessageStatus?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DlqMessage>>.Success(messages));

        var result = await _controller.Export("json");
        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("application/json");
        file.FileDownloadName.Should().Be("dlq-export.json");
    }

    [Fact]
    public async Task Export_Csv_ReturnsCsvFile()
    {
        var messages = new List<DlqMessage> { CreateTestMessage(1) };

        _historyService.Setup(s => s.ExportAsync(
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<DlqMessageStatus?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DlqMessage>>.Success(messages));

        var result = await _controller.Export("csv");
        var file = result.Should().BeOfType<FileContentResult>().Subject;
        file.ContentType.Should().Be("text/csv");
        file.FileDownloadName.Should().Be("dlq-export.csv");
    }

    [Fact]
    public async Task Export_Failure_ReturnsError()
    {
        _historyService.Setup(s => s.ExportAsync(
            It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
            It.IsAny<DateTimeOffset?>(), It.IsAny<DlqMessageStatus?>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DlqMessage>>.Failure(
                Error.Internal("EXPORT_ERROR", "Export failed")));

        var result = await _controller.Export();
        result.Should().NotBeOfType<FileContentResult>();
    }

    // ── GetSummary ──────────────────────────────────────────

    [Fact]
    public async Task GetSummary_Success_ReturnsSummary()
    {
        var summary = new DlqSummary(
            TotalMessages: 100,
            ActiveMessages: 50,
            ReplayedMessages: 30,
            ArchivedMessages: 20,
            ByCategory: new Dictionary<string, int> { ["MaxDelivery"] = 40, ["Transient"] = 60 },
            ByEntity: new Dictionary<string, int> { ["test-queue"] = 100 },
            OldestMessage: DateTimeOffset.UtcNow.AddDays(-30),
            NewestMessage: DateTimeOffset.UtcNow,
            DailyTrend: new List<DlqTrendPoint>
            {
                new(DateTimeOffset.UtcNow.Date, 5, 3)
            });

        _historyService.Setup(s => s.GetSummaryAsync(It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSummary>.Success(summary));

        var result = await _controller.GetSummary();
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<DlqSummaryResponse>().Subject;
        response.TotalMessages.Should().Be(100);
        response.ActiveMessages.Should().Be(50);
        response.DailyTrend.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSummary_Failure_ReturnsError()
    {
        _historyService.Setup(s => s.GetSummaryAsync(It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSummary>.Failure(Error.Internal("ERR", "Failed")));

        var result = await _controller.GetSummary();
        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    // ── GetForensicResult ───────────────────────────────────

    [Fact]
    public async Task GetForensicResult_Success_ReturnsForensicAnalysis()
    {
        var msg = CreateTestMessage(1);
        _historyService.Setup(s => s.GetByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqMessage>.Success(msg));

        var forensicResult = new ForensicEngineResult(
            FailureCategory.MaxDelivery, 0.95, "Max delivery count exceeded", "RequiresReview", "Deterministic");
        _forensicEngine.Setup(f => f.Analyse(msg)).Returns(forensicResult);

        _historyService.Setup(s => s.UpdateForensicResultAsync(
            1, FailureCategory.MaxDelivery, 0.95, "Max delivery count exceeded", "RequiresReview",
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqMessage>.Success(msg));

        var result = await _controller.GetForensicResult(1);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ForensicResultResponse>().Subject;
        response.FailureCategory.Should().Be("MaxDelivery");
        response.Confidence.Should().Be(0.95);
        response.Tier.Should().Be("Deterministic");
    }

    [Fact]
    public async Task GetForensicResult_NotFound_ReturnsError()
    {
        _historyService.Setup(s => s.GetByIdAsync(99, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqMessage>.Failure(Error.NotFound("NOT_FOUND", "Not found")));

        var result = await _controller.GetForensicResult(99);
        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    // ── AnalyseBatch ────────────────────────────────────────

    [Fact]
    public async Task AnalyseBatch_Success_ReturnsBatchSummary()
    {
        var nsId = Guid.NewGuid();
        var messages = new List<DlqMessage> { CreateTestMessage(1), CreateTestMessage(2) };
        var pageResult = new DlqHistoryPageResult(
            Items: messages, TotalCount: 2, Page: 1, PageSize: 200,
            HasNextPage: false, HasPreviousPage: false);

        _historyService.Setup(s => s.GetHistoryAsync(
            nsId, null, null, null, DlqMessageStatus.Active, null, 1, 200,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqHistoryPageResult>.Success(pageResult));

        var forensicResult = new ForensicEngineResult(
            FailureCategory.MaxDelivery, 0.9, "Max delivery", "RequiresReview", "Deterministic");
        _forensicEngine.Setup(f => f.Analyse(It.IsAny<DlqMessage>())).Returns(forensicResult);

        _historyService.Setup(s => s.UpdateForensicResultAsync(
            It.IsAny<long>(), It.IsAny<FailureCategory>(), It.IsAny<double>(),
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqMessage>.Success(CreateTestMessage()));

        var result = await _controller.AnalyseBatch(nsId);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<ForensicBatchSummaryResponse>().Subject;
        response.Analysed.Should().Be(2);
        response.Updated.Should().Be(2);
    }

    [Fact]
    public async Task AnalyseBatch_Failure_ReturnsError()
    {
        var nsId = Guid.NewGuid();
        _historyService.Setup(s => s.GetHistoryAsync(
            nsId, null, null, null, DlqMessageStatus.Active, null, 1, 200,
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqHistoryPageResult>.Failure(
                Error.Internal("ERR", "Scan failed")));

        var result = await _controller.AnalyseBatch(nsId);
        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    // ── TriggerScan ─────────────────────────────────────────

    [Fact]
    public async Task TriggerScan_Success_ReturnsCount()
    {
        var nsId = Guid.NewGuid();
        var mockMonitor = new Mock<IDlqMonitorService>();
        mockMonitor.Setup(s => s.ScanNamespaceAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Success(5));

        var services = new ServiceCollection();
        services.AddSingleton(mockMonitor.Object);
        var serviceProvider = services.BuildServiceProvider();

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = serviceProvider }
        };

        var result = await _controller.TriggerScan(nsId);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        ok.Value.Should().Be(5);
    }

    [Fact]
    public async Task TriggerScan_Failure_ReturnsNonOkResult()
    {
        var nsId = Guid.NewGuid();
        var mockMonitor = new Mock<IDlqMonitorService>();
        mockMonitor.Setup(s => s.ScanNamespaceAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<int>.Failure(Error.Internal("SCAN_ERR", "Scan failed")));

        var services = new ServiceCollection();
        services.AddSingleton(mockMonitor.Object);
        var serviceProvider = services.BuildServiceProvider();

        var controller = new DlqHistoryController(
            _historyService.Object,
            _forensicEngine.Object,
            _logger.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = serviceProvider }
        };

        // Problem() requires ProblemDetailsFactory, which returns ObjectResult
        // In test context without ProblemDetailsFactory, it throws.
        // Verify that a non-success result causes non-Ok behavior.
        var act = () => controller.TriggerScan(nsId);
        // Problem() without ProblemDetailsFactory will throw InvalidOperationException
        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    // ── GetTrend ────────────────────────────────────────────

    [Fact]
    public async Task GetTrend_ReturnsEmptyArrayWhenNoData()
    {
        var summary = new DlqSummary(
            TotalMessages: 0, ActiveMessages: 0, ReplayedMessages: 0, ArchivedMessages: 0,
            ByCategory: new Dictionary<string, int>(),
            ByEntity: new Dictionary<string, int>(),
            OldestMessage: null, NewestMessage: null,
            DailyTrend: new List<DlqTrendPoint>());

        _historyService.Setup(s => s.GetSummaryAsync(
            It.IsAny<Guid?>(), 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSummary>.Success(summary));

        var result = await _controller.GetTrend(days: 7);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var trend = ok.Value.Should().BeAssignableTo<IReadOnlyList<DlqTrendPointResponse>>().Subject;
        trend.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTrend_Returns7DayGrouping()
    {
        var trendPoints = Enumerable.Range(0, 7).Select(i =>
            new DlqTrendPoint(
                Date: DateTimeOffset.UtcNow.AddDays(-6 + i),
                NewMessages: i + 1,
                ResolvedMessages: i)).ToList();

        var summary = new DlqSummary(
            TotalMessages: 28, ActiveMessages: 10, ReplayedMessages: 15, ArchivedMessages: 3,
            ByCategory: new Dictionary<string, int>(),
            ByEntity: new Dictionary<string, int>(),
            OldestMessage: DateTimeOffset.UtcNow.AddDays(-6), NewestMessage: DateTimeOffset.UtcNow,
            DailyTrend: trendPoints);

        _historyService.Setup(s => s.GetSummaryAsync(
            It.IsAny<Guid?>(), 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSummary>.Success(summary));

        var result = await _controller.GetTrend(days: 7);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var trend = ok.Value.Should().BeAssignableTo<IReadOnlyList<DlqTrendPointResponse>>().Subject;
        trend.Should().HaveCount(7);
        trend[0].NewMessages.Should().Be(1);
        trend[6].NewMessages.Should().Be(7);
    }

    [Fact]
    public async Task GetTrend_FiltersByNamespaceId()
    {
        var nsId = Guid.NewGuid();
        var summary = new DlqSummary(
            TotalMessages: 5, ActiveMessages: 3, ReplayedMessages: 2, ArchivedMessages: 0,
            ByCategory: new Dictionary<string, int>(),
            ByEntity: new Dictionary<string, int>(),
            OldestMessage: null, NewestMessage: null,
            DailyTrend: new List<DlqTrendPoint>
            {
                new(DateTimeOffset.UtcNow.AddDays(-1), 3, 1),
                new(DateTimeOffset.UtcNow, 2, 1),
            });

        _historyService.Setup(s => s.GetSummaryAsync(
            nsId, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSummary>.Success(summary));

        var result = await _controller.GetTrend(namespaceId: nsId, days: 7);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var trend = ok.Value.Should().BeAssignableTo<IReadOnlyList<DlqTrendPointResponse>>().Subject;
        trend.Should().HaveCount(2);

        _historyService.Verify(s => s.GetSummaryAsync(nsId, 7, It.IsAny<CancellationToken>()), Times.Once);
    }
}
