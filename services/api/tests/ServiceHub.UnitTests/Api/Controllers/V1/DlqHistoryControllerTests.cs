using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Helpers;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class DlqHistoryControllerTests
{
    private readonly Mock<IDlqHistoryService> _historyService = new();
    private readonly Mock<ILogger<DlqHistoryController>> _logger = new();
    private readonly Mock<IDlqSignatureAnalysisService> _signatureAnalysisService = new();
    private readonly Mock<INamespaceRepository> _namespaceRepository = new();
    private readonly Mock<IFailureKnowledgeService> _knowledgeService = new();
    private readonly Mock<INamespaceSignatureLookupService> _signatureLookupService = new();
    private readonly Mock<ISignatureLifecycleService> _lifecycleService = new();
    private readonly Mock<ISignatureReplayService> _signatureReplayService = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly DlqHistoryController _controller;

    public DlqHistoryControllerTests()
    {
        // Configure knowledge service to return empty dict by default (no knowledge stored)
        _knowledgeService
            .Setup(x => x.GetKnowledgeBatchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Result<IReadOnlyDictionary<string, FailureKnowledge>>.Success(new Dictionary<string, FailureKnowledge>())));

        // Default lifecycle status: nothing transitioned (every hash reports Active).
        _lifecycleService
            .Setup(x => x.GetStatusBatchAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<IReadOnlyList<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyDictionary<string, SignatureLifecycleSnapshot>>.Success(
                new Dictionary<string, SignatureLifecycleSnapshot>()));
        _lifecycleService
            .Setup(x => x.GetStatusAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SignatureLifecycleSnapshot>.Success(
                new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null)));
        _lifecycleService
            .Setup(x => x.GetHistoryAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SignatureLifecycleEvent>>.Success([]));

        // Default replay history: no jobs (most tests don't care about replay events).
        _signatureReplayService
            .Setup(x => x.ListJobsAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaginatedResponse<BulkOperationJobResponse>>.Success(
                new PaginatedResponse<BulkOperationJobResponse>([], 0, 1, 100, false, false)));

        _controller = new DlqHistoryController(
            _historyService.Object,
            _logger.Object,
            _signatureAnalysisService.Object,
            _namespaceRepository.Object,
            _cache,
            _knowledgeService.Object,
            _signatureLookupService.Object,
            _lifecycleService.Object,
            _signatureReplayService.Object);
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    private static Namespace CreateOwnedNamespace(Guid id)
    {
        var ns = Namespace.Create("test-namespace", "PROTECTED:encrypted-data").Value;
        typeof(Namespace).GetProperty(nameof(Namespace.Id))!.SetValue(ns, id);
        return ns;
    }

    private static DlqMessage CreateTestMessage(long id = 1)
    {
        return new DlqMessage
        {
            MessageId = $"msg-{id}",
            SequenceNumber = id,
            BodyHash = $"hash-{id}",
            NamespaceId = Guid.NewGuid(),
            OwnerId = TestConstants.TestOwnerId,
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
        var act = () => new DlqHistoryController(
            null!, _logger.Object, _signatureAnalysisService.Object, _namespaceRepository.Object, _cache, _knowledgeService.Object,
            _signatureLookupService.Object, _lifecycleService.Object, _signatureReplayService.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("historyService");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new DlqHistoryController(
            _historyService.Object, null!, _signatureAnalysisService.Object, _namespaceRepository.Object, _cache, _knowledgeService.Object,
            _signatureLookupService.Object, _lifecycleService.Object, _signatureReplayService.Object);
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
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
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
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
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
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
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
        _historyService.Setup(s => s.GetByIdAsync(It.IsAny<string>(), 42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqMessage>.Success(msg));

        var result = await _controller.GetById(42);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<DlqMessageDetailResponse>().Subject;
        response.MessageId.Should().Be("msg-42");
    }

    [Fact]
    public async Task GetById_NotFound_ReturnsError()
    {
        _historyService.Setup(s => s.GetByIdAsync(It.IsAny<string>(), 99, It.IsAny<CancellationToken>()))
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

        _historyService.Setup(s => s.GetTimelineAsync(It.IsAny<string>(), 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DlqTimelineEvent>>.Success(events));

        var result = await _controller.GetTimeline(1);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<DlqTimelineResponse>().Subject;
        response.Events.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTimeline_Failure_ReturnsError()
    {
        _historyService.Setup(s => s.GetTimelineAsync(It.IsAny<string>(), 99, It.IsAny<CancellationToken>()))
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

        _historyService.Setup(s => s.UpdateNotesAsync(It.IsAny<string>(), 1, "Updated note", It.IsAny<CancellationToken>()))
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
        _historyService.Setup(s => s.UpdateNotesAsync(It.IsAny<string>(), 99, "note", It.IsAny<CancellationToken>()))
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
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
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
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
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
            It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string?>(), It.IsAny<DateTimeOffset?>(),
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

        _historyService.Setup(s => s.GetSummaryAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
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
        _historyService.Setup(s => s.GetSummaryAsync(It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSummary>.Failure(Error.Internal("ERR", "Failed")));

        var result = await _controller.GetSummary();
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
            _logger.Object,
            _signatureAnalysisService.Object,
            _namespaceRepository.Object,
            _cache,
            _knowledgeService.Object,
            _signatureLookupService.Object,
            _lifecycleService.Object,
            _signatureReplayService.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { RequestServices = serviceProvider }
        };

        // Problem() requires ProblemDetailsFactory, which returns ObjectResult
        // In test context without ProblemDetailsFactory, it throws.
        // Verify that a non-success result causes non-Ok behavior.
        var act = () => controller.TriggerScan(nsId);
        // ToActionResult() properly returns a 500 ObjectResult (no exception thrown)
        var result = await act.Should().NotThrowAsync();
        result.Subject.Result.Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
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
            It.IsAny<string>(), It.IsAny<Guid?>(), 7, It.IsAny<CancellationToken>()))
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
            It.IsAny<string>(), It.IsAny<Guid?>(), 7, It.IsAny<CancellationToken>()))
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
            It.IsAny<string>(), nsId, 7, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSummary>.Success(summary));

        var result = await _controller.GetTrend(namespaceId: nsId, days: 7);
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var trend = ok.Value.Should().BeAssignableTo<IReadOnlyList<DlqTrendPointResponse>>().Subject;
        trend.Should().HaveCount(2);

        _historyService.Verify(s => s.GetSummaryAsync(It.IsAny<string>(), nsId, 7, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetSignatures ───────────────────────────────────────

    private static DlqSignatureAnalysisResult CreateAvailableAnalysis() => new(
        Available: true,
        Method: "clustered",
        BatchSize: 5,
        Clusters:
        [
            new DlqClusterSignature(
                Size: 4,
                MessageIds: [1, 2, 3, 4],
                DominantEntity: "orders-queue",
                DominantDeadletterReason: "MaxDeliveryCountExceeded",
                DominantDeadletterReasonCount: 4,
                TopTerms: ["timeout"],
                IsNew: true,
                FirstSeenAt: DateTimeOffset.UtcNow,
                OccurrenceCount: 1,
                WindowStart: DateTimeOffset.UtcNow.AddHours(-1),
                WindowEnd: DateTimeOffset.UtcNow,
                Explanation: "4 messages: max delivery count exceeded on orders-queue.")
        ],
        Singletons: [new DlqSingletonSignature(5, "orders-queue", "TTLExpiredException")]);

    [Fact]
    public void Constructor_NullSignatureAnalysisService_Throws()
    {
        var act = () => new DlqHistoryController(
            _historyService.Object, _logger.Object, null!, _namespaceRepository.Object, _cache, _knowledgeService.Object,
            _signatureLookupService.Object, _lifecycleService.Object, _signatureReplayService.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("signatureAnalysisService");
    }

    [Fact]
    public void Constructor_NullNamespaceRepository_Throws()
    {
        var act = () => new DlqHistoryController(
            _historyService.Object, _logger.Object, _signatureAnalysisService.Object, null!, _cache, _knowledgeService.Object,
            _signatureLookupService.Object, _lifecycleService.Object, _signatureReplayService.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullCache_Throws()
    {
        var act = () => new DlqHistoryController(
            _historyService.Object, _logger.Object, _signatureAnalysisService.Object, _namespaceRepository.Object, null!, _knowledgeService.Object,
            _signatureLookupService.Object, _lifecycleService.Object, _signatureReplayService.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("cache");
    }

    [Fact]
    public async Task GetSignatures_Success_ReturnsSignaturesResponse()
    {
        var nsId = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureAnalysisService.Setup(s => s.AnalyzeAsync(It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSignatureAnalysisResult>.Success(CreateAvailableAnalysis()));

        var result = await _controller.GetSignatures(nsId);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<DlqSignaturesResponse>().Subject;
        response.Available.Should().BeTrue();
        response.Method.Should().Be("clustered");
        response.Clusters.Should().HaveCount(1);
        response.Clusters[0].MessageIds.Should().BeEquivalentTo(new long[] { 1, 2, 3, 4 });
        response.Singletons.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetSignatures_AIUnavailable_Returns200WithAvailableFalse()
    {
        var nsId = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureAnalysisService.Setup(s => s.AnalyzeAsync(It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSignatureAnalysisResult>.Success(new DlqSignatureAnalysisResult(
                Available: false, Method: null, BatchSize: 5, Clusters: [], Singletons: [])));

        var result = await _controller.GetSignatures(nsId);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<DlqSignaturesResponse>().Subject;
        response.Available.Should().BeFalse();
        response.Clusters.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSignatures_NamespaceNotOwned_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Namespace not found")));

        var result = await _controller.GetSignatures(nsId);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
        _signatureAnalysisService.Verify(s => s.AnalyzeAsync(
            It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSignatures_SecondCallWithinCacheWindow_DoesNotReanalyze()
    {
        var nsId = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureAnalysisService.Setup(s => s.AnalyzeAsync(It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSignatureAnalysisResult>.Success(CreateAvailableAnalysis()));

        var first = await _controller.GetSignatures(nsId);
        var second = await _controller.GetSignatures(nsId);

        first.Result.Should().BeOfType<OkObjectResult>();
        second.Result.Should().BeOfType<OkObjectResult>();
        _signatureAnalysisService.Verify(s => s.AnalyzeAsync(
            It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetSignatureDetail ──────────────────────────────────

    private static readonly string ClusteredHash =
        ClusterSignatureHasher.ComputeHash(["timeout"], "MaxDeliveryCountExceeded");

    [Fact]
    public async Task GetSignatureDetail_Clustered_ReturnsDetailWithRelatedMessages()
    {
        var nsId = Guid.NewGuid();
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureAnalysisService.Setup(s => s.AnalyzeAsync(It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSignatureAnalysisResult>.Success(CreateAvailableAnalysis()));
        _historyService.Setup(s => s.GetByIdsAsync(It.IsAny<string>(), It.Is<IReadOnlyList<long>>(ids => ids.Count == 4), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DlqMessage>>.Success(
                new List<DlqMessage> { CreateTestMessage(1), CreateTestMessage(2) }));

        var result = await _controller.GetSignatureDetail(nsId, ClusteredHash);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<DlqSignatureDetailResponse>().Subject;
        response.SignatureHash.Should().Be(ClusteredHash);
        response.IsCurrentlyClustered.Should().BeTrue();
        response.RelatedMessages.Should().HaveCount(2);
        response.Status.Should().Be("Active");
        response.Confidence.Should().Be("High");
    }

    [Fact]
    public async Task GetSignatureDetail_NotClustered_FallsBackToPersistedRecord()
    {
        var nsId = Guid.NewGuid();
        const string hash = "no-longer-clustered-hash";
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureAnalysisService.Setup(s => s.AnalyzeAsync(It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSignatureAnalysisResult>.Success(CreateAvailableAnalysis()));
        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NamespaceSignature
            {
                NamespaceId = nsId,
                OwnerId = Namespace.SpaOwnerId,
                SignatureHash = hash,
                FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-10),
                LastSeenAt = DateTimeOffset.UtcNow.AddDays(-5),
                OccurrenceCount = 3,
                DominantDeadletterReason = "TTLExpiredException",
                TopTermsJson = "[\"expired\"]",
            });
        _knowledgeService.Setup(s => s.GetKnowledgeAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FailureKnowledge>.Success(new FailureKnowledge(
                null, null, null, null, null, null, null, 0, null, null)));

        var result = await _controller.GetSignatureDetail(nsId, hash);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<DlqSignatureDetailResponse>().Subject;
        response.IsCurrentlyClustered.Should().BeFalse();
        response.RelatedMessages.Should().BeEmpty();
        response.OccurrenceCount.Should().Be(3);
        response.DominantDeadletterReason.Should().Be("TTLExpiredException");
    }

    [Fact]
    public async Task GetSignatureDetail_NeverObserved_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        const string hash = "never-seen-hash";
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureAnalysisService.Setup(s => s.AnalyzeAsync(It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSignatureAnalysisResult>.Success(CreateAvailableAnalysis()));
        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NamespaceSignature?)null);

        var result = await _controller.GetSignatureDetail(nsId, hash);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── GetSignatureTimeline ─────────────────────────────────

    [Fact]
    public async Task GetSignatureTimeline_MergesEventsInAscendingOrder()
    {
        var nsId = Guid.NewGuid();
        const string hash = "timeline-hash";
        var firstSeen = DateTimeOffset.UtcNow.AddDays(-10);
        var lastSeen = DateTimeOffset.UtcNow.AddDays(-1);

        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NamespaceSignature
            {
                NamespaceId = nsId,
                OwnerId = Namespace.SpaOwnerId,
                SignatureHash = hash,
                FirstSeenAt = firstSeen,
                LastSeenAt = lastSeen,
                OccurrenceCount = 4,
                DominantDeadletterReason = "MaxDeliveryCountExceeded",
                TopTermsJson = "[\"timeout\"]",
            });
        _knowledgeService.Setup(s => s.GetKnowledgeAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FailureKnowledge>.Success(new FailureKnowledge(
                null, null, null, null, null, null, null, 0, null, null)));
        _lifecycleService.Setup(s => s.GetHistoryAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SignatureLifecycleEvent>>.Success(
            [
                new SignatureLifecycleEvent(
                    SignatureLifecycleStatus.Active, SignatureLifecycleStatus.Resolved,
                    DateTimeOffset.UtcNow, "fixed it"),
            ]));

        var result = await _controller.GetSignatureTimeline(nsId, hash);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<SignatureTimelineResponse>().Subject;
        response.Events.Should().HaveCount(3);
        response.Events.Select(e => e.Timestamp).Should().BeInAscendingOrder();
        response.Events[0].EventType.Should().Be("SignatureFirstObserved");
        response.Events[^1].EventType.Should().Be("StatusChanged");
    }

    [Fact]
    public async Task GetSignatureTimeline_IncludesReplayJobEvents_MergedChronologically()
    {
        var nsId = Guid.NewGuid();
        const string hash = "timeline-hash-with-replays";
        var firstSeen = DateTimeOffset.UtcNow.AddDays(-10);
        var lastSeen = DateTimeOffset.UtcNow.AddDays(-1);
        var replayCreatedAt = DateTimeOffset.UtcNow.AddDays(-5);
        var replayCompletedAt = DateTimeOffset.UtcNow.AddDays(-5).AddMinutes(2);

        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NamespaceSignature
            {
                NamespaceId = nsId,
                OwnerId = Namespace.SpaOwnerId,
                SignatureHash = hash,
                FirstSeenAt = firstSeen,
                LastSeenAt = lastSeen,
                OccurrenceCount = 4,
                DominantDeadletterReason = "MaxDeliveryCountExceeded",
                TopTermsJson = "[\"timeout\"]",
            });
        _knowledgeService.Setup(s => s.GetKnowledgeAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FailureKnowledge>.Success(new FailureKnowledge(
                null, null, null, null, null, null, null, 0, null, null)));
        _lifecycleService.Setup(s => s.GetHistoryAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SignatureLifecycleEvent>>.Success([]));
        _signatureReplayService
            .Setup(x => x.ListJobsAsync(It.IsAny<string>(), nsId, hash, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaginatedResponse<BulkOperationJobResponse>>.Success(
                new PaginatedResponse<BulkOperationJobResponse>(
                    [
                        new BulkOperationJobResponse(
                            Id: Guid.NewGuid(),
                            OperationType: "Replay",
                            Status: "Completed",
                            NamespaceId: nsId,
                            NamespaceDisplayName: "test-namespace",
                            EntityNameFilter: null,
                            StatusFilter: null,
                            CategoryFilter: null,
                            From: null,
                            To: null,
                            TotalMatched: 4,
                            ProcessedCount: 4,
                            SuccessCount: 4,
                            FailureCount: 0,
                            SkippedCount: 0,
                            FailureSample: null,
                            ErrorSummary: null,
                            CreatedAt: replayCreatedAt,
                            StartedAt: replayCreatedAt,
                            CompletedAt: replayCompletedAt,
                            IsCancellable: false),
                    ],
                    1, 1, 100, false, false)));

        var result = await _controller.GetSignatureTimeline(nsId, hash);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<SignatureTimelineResponse>().Subject;
        response.Events.Should().HaveCount(4);
        response.Events.Select(e => e.Timestamp).Should().BeInAscendingOrder();
        response.Events.Select(e => e.EventType).Should().Contain(["ReplayJobStarted", "ReplayJobCompleted"]);
        response.Events.First(e => e.EventType == "ReplayJobStarted").Timestamp.Should().Be(replayCreatedAt);
        response.Events.First(e => e.EventType == "ReplayJobCompleted").Timestamp.Should().Be(replayCompletedAt);
    }

    [Fact]
    public async Task GetSignatureTimeline_ReplayJobStillRunning_OnlyEmitsStartedEvent()
    {
        var nsId = Guid.NewGuid();
        const string hash = "timeline-hash-running-replay";
        var firstSeen = DateTimeOffset.UtcNow.AddDays(-3);

        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new NamespaceSignature
            {
                NamespaceId = nsId,
                OwnerId = Namespace.SpaOwnerId,
                SignatureHash = hash,
                FirstSeenAt = firstSeen,
                LastSeenAt = firstSeen,
                OccurrenceCount = 1,
                DominantDeadletterReason = "MaxDeliveryCountExceeded",
                TopTermsJson = "[\"timeout\"]",
            });
        _knowledgeService.Setup(s => s.GetKnowledgeAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FailureKnowledge>.Success(new FailureKnowledge(
                null, null, null, null, null, null, null, 0, null, null)));
        _lifecycleService.Setup(s => s.GetHistoryAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SignatureLifecycleEvent>>.Success([]));
        _signatureReplayService
            .Setup(x => x.ListJobsAsync(It.IsAny<string>(), nsId, hash, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaginatedResponse<BulkOperationJobResponse>>.Success(
                new PaginatedResponse<BulkOperationJobResponse>(
                    [
                        new BulkOperationJobResponse(
                            Id: Guid.NewGuid(),
                            OperationType: "Replay",
                            Status: "Running",
                            NamespaceId: nsId,
                            NamespaceDisplayName: "test-namespace",
                            EntityNameFilter: null,
                            StatusFilter: null,
                            CategoryFilter: null,
                            From: null,
                            To: null,
                            TotalMatched: 1,
                            ProcessedCount: 0,
                            SuccessCount: 0,
                            FailureCount: 0,
                            SkippedCount: 0,
                            FailureSample: null,
                            ErrorSummary: null,
                            CreatedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                            StartedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
                            CompletedAt: null,
                            IsCancellable: true),
                    ],
                    1, 1, 100, false, false)));

        var result = await _controller.GetSignatureTimeline(nsId, hash);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<SignatureTimelineResponse>().Subject;
        response.Events.Should().ContainSingle(e => e.EventType == "ReplayJobStarted");
        response.Events.Should().NotContain(e =>
            e.EventType == "ReplayJobCompleted" || e.EventType == "ReplayJobFailed" || e.EventType == "ReplayJobCancelled");
    }

    [Fact]
    public async Task GetSignatureTimeline_NeverObserved_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        const string hash = "never-seen-hash";
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NamespaceSignature?)null);
        _lifecycleService.Setup(s => s.GetHistoryAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SignatureLifecycleEvent>>.Success([]));

        var result = await _controller.GetSignatureTimeline(nsId, hash);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── GetSignatureRootCauseMatches ──────────────────────────

    private static NamespaceSignature MakeSignature(Guid namespaceId, string hash, int occurrenceCount = 1) =>
        new()
        {
            NamespaceId = namespaceId,
            OwnerId = Namespace.SpaOwnerId,
            SignatureHash = hash,
            FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-3),
            LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
            OccurrenceCount = occurrenceCount,
            DominantDeadletterReason = "MaxDeliveryCountExceeded",
            TopTermsJson = "[\"timeout\",\"sql\"]",
        };

    [Fact]
    public async Task GetSignatureRootCauseMatches_SignatureNotFound_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        const string hash = "unknown-hash";
        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync((NamespaceSignature?)null);

        var result = await _controller.GetSignatureRootCauseMatches(nsId, hash);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task GetSignatureRootCauseMatches_NoMatchesElsewhere_ReturnsEmptyMatchesWithFleetTotalEqualToLocal()
    {
        var nsId = Guid.NewGuid();
        const string hash = "solo-hash";
        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSignature(nsId, hash, occurrenceCount: 5));
        _signatureLookupService.Setup(s => s.FindAcrossNamespacesAsync(It.IsAny<string>(), hash, nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<NamespaceSignature>)[]);

        var result = await _controller.GetSignatureRootCauseMatches(nsId, hash);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RootCauseExplorerResponse>().Subject;
        response.Matches.Should().BeEmpty();
        response.TotalOccurrencesAcrossFleet.Should().Be(5);
        response.DominantDeadletterReason.Should().Be("MaxDeliveryCountExceeded");
        response.TopTerms.Should().BeEquivalentTo(new[] { "timeout", "sql" });
    }

    [Fact]
    public async Task GetSignatureRootCauseMatches_MatchInOtherNamespace_IncludesItWithKnowledgeAndStatus()
    {
        var nsId = Guid.NewGuid();
        var otherNsId = Guid.NewGuid();
        const string hash = "shared-hash";

        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSignature(nsId, hash, occurrenceCount: 3));
        var otherSignature = MakeSignature(otherNsId, hash, occurrenceCount: 7);
        _signatureLookupService.Setup(s => s.FindAcrossNamespacesAsync(It.IsAny<string>(), hash, nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<NamespaceSignature>)[otherSignature]);
        _knowledgeService.Setup(s => s.GetKnowledgeAsync(It.IsAny<string>(), otherNsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FailureKnowledge>.Success(new FailureKnowledge(
                RootCause: "Downstream service returned 500s during deploy window",
                ResolutionNotes: "Rolled back the deploy",
                OperationalNotes: null,
                RunbookLink: null,
                Owner: "team-payments",
                ReplayGuidance: "Safe",
                LastUpdatedAt: DateTimeOffset.UtcNow,
                KnowledgeVersion: 2,
                ReviewDueAt: null,
                Tags: null)));
        _lifecycleService.Setup(s => s.GetStatusAsync(It.IsAny<string>(), otherNsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SignatureLifecycleSnapshot>.Success(
                new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Resolved, SignatureLifecycleStatus.Active, DateTimeOffset.UtcNow, "fixed")));

        var result = await _controller.GetSignatureRootCauseMatches(nsId, hash);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RootCauseExplorerResponse>().Subject;
        response.TotalOccurrencesAcrossFleet.Should().Be(10);
        response.Matches.Should().ContainSingle();
        var match = response.Matches[0];
        match.NamespaceId.Should().Be(otherNsId);
        match.OccurrenceCount.Should().Be(7);
        match.LifecycleStatus.Should().Be(nameof(SignatureLifecycleStatus.Resolved));
        match.Knowledge.Should().NotBeNull();
        match.Knowledge!.RootCause.Should().Be("Downstream service returned 500s during deploy window");
        match.Knowledge.Owner.Should().Be("team-payments");
    }

    [Fact]
    public async Task GetSignatureRootCauseMatches_MatchWithNoRecordedRootCause_HasNullKnowledge()
    {
        var nsId = Guid.NewGuid();
        var otherNsId = Guid.NewGuid();
        const string hash = "shared-hash-no-knowledge";

        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSignature(nsId, hash));
        var otherSignature = MakeSignature(otherNsId, hash);
        _signatureLookupService.Setup(s => s.FindAcrossNamespacesAsync(It.IsAny<string>(), hash, nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<NamespaceSignature>)[otherSignature]);
        _knowledgeService.Setup(s => s.GetKnowledgeAsync(It.IsAny<string>(), otherNsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FailureKnowledge>.Success(new FailureKnowledge(
                null, null, null, null, null, null, null, 1, null, null)));
        _lifecycleService.Setup(s => s.GetStatusAsync(It.IsAny<string>(), otherNsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SignatureLifecycleSnapshot>.Success(
                new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null)));

        var result = await _controller.GetSignatureRootCauseMatches(nsId, hash);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RootCauseExplorerResponse>().Subject;
        response.Matches.Should().ContainSingle();
        response.Matches[0].Knowledge.Should().BeNull();
    }

    [Fact]
    public async Task GetSignatureRootCauseMatches_MatchWithNoReplayHistory_HasNullLastReplayOutcome()
    {
        var nsId = Guid.NewGuid();
        var otherNsId = Guid.NewGuid();
        const string hash = "shared-hash-no-replay";

        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSignature(nsId, hash));
        var otherSignature = MakeSignature(otherNsId, hash);
        _signatureLookupService.Setup(s => s.FindAcrossNamespacesAsync(It.IsAny<string>(), hash, nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<NamespaceSignature>)[otherSignature]);
        _knowledgeService.Setup(s => s.GetKnowledgeAsync(It.IsAny<string>(), otherNsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FailureKnowledge>.Success(new FailureKnowledge(
                null, null, null, null, null, null, null, 1, null, null)));
        _lifecycleService.Setup(s => s.GetStatusAsync(It.IsAny<string>(), otherNsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SignatureLifecycleSnapshot>.Success(
                new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null)));
        // Default constructor mock already returns an empty replay history for any args.

        var result = await _controller.GetSignatureRootCauseMatches(nsId, hash);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RootCauseExplorerResponse>().Subject;
        response.Matches.Should().ContainSingle();
        response.Matches[0].LastReplayOutcome.Should().BeNull();
    }

    [Fact]
    public async Task GetSignatureRootCauseMatches_MatchWithReplayHistory_IncludesLastReplayOutcome()
    {
        var nsId = Guid.NewGuid();
        var otherNsId = Guid.NewGuid();
        const string hash = "shared-hash-with-replay";
        var lastReplayAt = DateTimeOffset.UtcNow.AddDays(-2);

        _signatureLookupService.Setup(s => s.GetByHashAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeSignature(nsId, hash));
        var otherSignature = MakeSignature(otherNsId, hash);
        _signatureLookupService.Setup(s => s.FindAcrossNamespacesAsync(It.IsAny<string>(), hash, nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<NamespaceSignature>)[otherSignature]);
        _knowledgeService.Setup(s => s.GetKnowledgeAsync(It.IsAny<string>(), otherNsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FailureKnowledge>.Success(new FailureKnowledge(
                null, null, null, null, null, null, null, 1, null, null)));
        _lifecycleService.Setup(s => s.GetStatusAsync(It.IsAny<string>(), otherNsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SignatureLifecycleSnapshot>.Success(
                new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null)));

        var job = new BulkOperationJobResponse(
            Id: Guid.NewGuid(),
            OperationType: "Replay",
            Status: "Completed",
            NamespaceId: otherNsId,
            NamespaceDisplayName: "other-namespace",
            EntityNameFilter: null,
            StatusFilter: null,
            CategoryFilter: null,
            From: null,
            To: null,
            TotalMatched: 5,
            ProcessedCount: 5,
            SuccessCount: 5,
            FailureCount: 0,
            SkippedCount: 0,
            FailureSample: null,
            ErrorSummary: null,
            CreatedAt: lastReplayAt,
            StartedAt: lastReplayAt,
            CompletedAt: lastReplayAt.AddMinutes(1),
            IsCancellable: false);
        _signatureReplayService.Setup(s => s.ListJobsAsync(
                It.IsAny<string>(), otherNsId, hash, 1, 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<PaginatedResponse<BulkOperationJobResponse>>.Success(
                new PaginatedResponse<BulkOperationJobResponse>([job], 1, 1, 1, false, false)));

        var result = await _controller.GetSignatureRootCauseMatches(nsId, hash);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<RootCauseExplorerResponse>().Subject;
        response.Matches.Should().ContainSingle();
        var outcome = response.Matches[0].LastReplayOutcome;
        outcome.Should().NotBeNull();
        outcome!.Status.Should().Be("Completed");
        outcome.CreatedAt.Should().Be(lastReplayAt);
    }

    // ── UpdateSignatureStatus ────────────────────────────────

    [Fact]
    public async Task UpdateSignatureStatus_ValidTransition_ReturnsOkAndInvalidatesCache()
    {
        var nsId = Guid.NewGuid();
        const string hash = "status-hash";
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureAnalysisService.Setup(s => s.AnalyzeAsync(It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSignatureAnalysisResult>.Success(CreateAvailableAnalysis()));
        _lifecycleService.Setup(s => s.TransitionAsync(
                It.IsAny<string>(), nsId, hash, SignatureLifecycleStatus.Resolved, "done", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SignatureLifecycleSnapshot>.Success(
                new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Resolved, SignatureLifecycleStatus.Active, DateTimeOffset.UtcNow, "done")));

        // Warm the 60s cache, then confirm the status update busts it.
        await _controller.GetSignatures(nsId);

        var result = await _controller.UpdateSignatureStatus(
            nsId, hash, new UpdateSignatureStatusRequest(SignatureLifecycleStatus.Resolved, "done"));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<SignatureLifecycleStatusResponse>().Subject;
        response.Status.Should().Be("Resolved");
        response.PreviousStatus.Should().Be("Active");

        await _controller.GetSignatures(nsId);
        _signatureAnalysisService.Verify(s => s.AnalyzeAsync(
            It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UpdateSignatureStatus_InvalidTransition_ReturnsBadRequest()
    {
        var nsId = Guid.NewGuid();
        const string hash = "status-hash";
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _lifecycleService.Setup(s => s.TransitionAsync(
                It.IsAny<string>(), nsId, hash, SignatureLifecycleStatus.Active, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SignatureLifecycleSnapshot>.Failure(
                Error.Validation("SignatureLifecycle.InvalidTransition", "Cannot transition")));

        var result = await _controller.UpdateSignatureStatus(
            nsId, hash, new UpdateSignatureStatusRequest(SignatureLifecycleStatus.Active));

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ── UpsertKnowledge ────────────────────────────────

    [Fact]
    public async Task UpsertKnowledge_ValidRequest_ReturnsOkAndInvalidatesCache()
    {
        var nsId = Guid.NewGuid();
        const string hash = "knowledge-hash";
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureAnalysisService.Setup(s => s.AnalyzeAsync(It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSignatureAnalysisResult>.Success(CreateAvailableAnalysis()));

        var persisted = new FailureKnowledge(
            RootCause: "Timeout", ResolutionNotes: null, OperationalNotes: null, RunbookLink: null,
            Owner: null, ReplayGuidance: null, LastUpdatedAt: DateTimeOffset.UtcNow, KnowledgeVersion: 1,
            ReviewDueAt: null, Tags: null, UpdatedBy: "alice@example.com");
        _knowledgeService.Setup(s => s.UpsertKnowledgeAsync(
                It.IsAny<string>(), nsId, hash, It.IsAny<FailureKnowledge>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FailureKnowledge>.Success(persisted));

        // Warm the 60s cache, then confirm the upsert busts it.
        await _controller.GetSignatures(nsId);

        var request = new UpsertKnowledgeRequest { RootCause = "Timeout", ChangedBy = "alice@example.com" };
        var result = await _controller.UpsertKnowledge(nsId, hash, request);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<KnowledgeResponse>().Subject;
        response.RootCause.Should().Be("Timeout");
        response.UpdatedBy.Should().Be("alice@example.com");

        await _controller.GetSignatures(nsId);
        _signatureAnalysisService.Verify(s => s.AnalyzeAsync(
            It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task UpsertKnowledge_NamespaceNotOwned_ReturnsNotFound()
    {
        var nsId = Guid.NewGuid();
        const string hash = "knowledge-hash";
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("NOT_FOUND", "Namespace not found")));

        var request = new UpsertKnowledgeRequest { RootCause = "Timeout" };
        var result = await _controller.UpsertKnowledge(nsId, hash, request);

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── GetKnowledgeHistory ────────────────────────────────

    [Fact]
    public async Task GetKnowledgeHistory_ReturnsEntriesFromService()
    {
        var nsId = Guid.NewGuid();
        const string hash = "knowledge-hash";
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));

        var entries = new List<FailureKnowledgeHistoryEntry>
        {
            new(KnowledgeVersion: 2, RootCause: "v2", ResolutionNotes: null, OperationalNotes: null,
                RunbookLink: null, Owner: null, ReplayGuidance: null, Tags: null, ReviewDueAt: null,
                UpdatedBy: "bob@example.com", UpdatedAt: DateTimeOffset.UtcNow),
            new(KnowledgeVersion: 1, RootCause: "v1", ResolutionNotes: null, OperationalNotes: null,
                RunbookLink: null, Owner: null, ReplayGuidance: null, Tags: null, ReviewDueAt: null,
                UpdatedBy: "alice@example.com", UpdatedAt: DateTimeOffset.UtcNow.AddDays(-1)),
        };
        _knowledgeService.Setup(s => s.GetKnowledgeHistoryAsync(It.IsAny<string>(), nsId, hash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<FailureKnowledgeHistoryEntry>>.Success(entries));

        var result = await _controller.GetKnowledgeHistory(nsId, hash);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeAssignableTo<IReadOnlyList<FailureKnowledgeHistoryResponse>>().Subject;
        response.Should().HaveCount(2);
        response[0].KnowledgeVersion.Should().Be(2);
        response[0].UpdatedBy.Should().Be("bob@example.com");
        response[1].KnowledgeVersion.Should().Be(1);
    }

    // ── MarkKnowledgeForReview ────────────────────────────────

    [Fact]
    public async Task MarkKnowledgeForReview_ValidRequest_ReturnsOkAndInvalidatesCache()
    {
        var nsId = Guid.NewGuid();
        const string hash = "knowledge-hash";
        var reviewDueAt = DateTimeOffset.UtcNow.AddDays(7);
        _namespaceRepository.Setup(r => r.GetByIdAsync(nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(CreateOwnedNamespace(nsId)));
        _signatureAnalysisService.Setup(s => s.AnalyzeAsync(It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSignatureAnalysisResult>.Success(CreateAvailableAnalysis()));

        var persisted = new FailureKnowledge(
            RootCause: null, ResolutionNotes: null, OperationalNotes: null, RunbookLink: null,
            Owner: null, ReplayGuidance: null, LastUpdatedAt: DateTimeOffset.UtcNow, KnowledgeVersion: 1,
            ReviewDueAt: reviewDueAt, Tags: null);
        _knowledgeService.Setup(s => s.MarkForReviewAsync(
                It.IsAny<string>(), nsId, hash, reviewDueAt, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FailureKnowledge>.Success(persisted));

        await _controller.GetSignatures(nsId);

        var result = await _controller.MarkKnowledgeForReview(nsId, hash, new MarkForReviewRequest(reviewDueAt));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<KnowledgeResponse>().Subject;
        response.ReviewDueAt.Should().Be(reviewDueAt);

        await _controller.GetSignatures(nsId);
        _signatureAnalysisService.Verify(s => s.AnalyzeAsync(
            It.IsAny<string>(), nsId, It.IsAny<CancellationToken>()), Times.Exactly(2));
    }
}
