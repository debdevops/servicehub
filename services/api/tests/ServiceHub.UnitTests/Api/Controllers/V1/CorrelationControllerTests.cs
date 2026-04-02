using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public class CorrelationControllerTests : IDisposable
{
    private readonly Mock<INamespaceRepository> _namespaceRepository;
    private readonly Mock<IServiceBusClientCache> _clientCache;
    private readonly Mock<IConnectionStringProtector> _connectionStringProtector;
    private readonly Mock<IServiceBusClientWrapper> _wrapper;
    private readonly Mock<ILogger<CorrelationController>> _logger;
    private readonly DlqDbContext _dlqContext;
    private readonly CorrelationController _controller;

    public CorrelationControllerTests()
    {
        _namespaceRepository = new Mock<INamespaceRepository>();
        _clientCache = new Mock<IServiceBusClientCache>();
        _connectionStringProtector = new Mock<IConnectionStringProtector>();
        _wrapper = new Mock<IServiceBusClientWrapper>();
        _logger = new Mock<ILogger<CorrelationController>>();

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _dlqContext = new DlqDbContext(options);

        _controller = new CorrelationController(
            _namespaceRepository.Object,
            _clientCache.Object,
            _connectionStringProtector.Object,
            _dlqContext,
            _logger.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
    }

    public void Dispose() => _dlqContext.Dispose();

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static Namespace CreateTestNamespace(string name = "test-namespace")
    {
        return Namespace.Create(
            name,
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            "Test NS").Value;
    }

    private static Message CreateTestMessage(string correlationId, string messageId = "msg-1")
    {
        return new Message
        {
            MessageId = messageId,
            SequenceNumber = 1,
            CorrelationId = correlationId,
            State = MessageState.Active,
            EnqueuedTime = DateTimeOffset.UtcNow.AddMinutes(-30),
            SizeInBytes = 512
        };
    }

    private static QueueRuntimePropertiesDto CreateTestQueue(string name = "test-queue")
    {
        return new QueueRuntimePropertiesDto(
            Name: name,
            ActiveMessageCount: 1,
            DeadLetterMessageCount: 0,
            ScheduledMessageCount: 0,
            TransferMessageCount: 0,
            TransferDeadLetterMessageCount: 0,
            SizeInBytes: 512,
            Status: "Active",
            CreatedAt: DateTimeOffset.UtcNow.AddDays(-7),
            UpdatedAt: DateTimeOffset.UtcNow,
            AccessedAt: DateTimeOffset.UtcNow,
            RequiresSession: false,
            RequiresDuplicateDetection: false,
            EnablePartitioning: false,
            EnableBatchedOperations: true,
            MaxSizeInMegabytes: 1024,
            MaxDeliveryCount: 10,
            DefaultMessageTimeToLive: TimeSpan.FromDays(14),
            LockDuration: TimeSpan.FromSeconds(30),
            AutoDeleteOnIdle: TimeSpan.MaxValue);
    }

    /// <summary>
    /// Configures the mock wrapper for a namespace to return a single queue with the given messages,
    /// and no topics.
    /// </summary>
    private void SetupDefaultWrapper(Namespace ns, IReadOnlyList<Message>? messages = null)
    {
        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Success("conn-string"));

        _clientCache.Setup(c => c.GetOrCreate(ns.Id, It.IsAny<string>()))
            .Returns(_wrapper.Object);

        _wrapper.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(
                new List<QueueRuntimePropertiesDto> { CreateTestQueue() }));

        _wrapper.Setup(w => w.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages ?? new List<Message>()));

        _wrapper.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(new List<TopicRuntimePropertiesDto>()));
    }

    // ─── Validation ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_NullCorrelationId_ReturnsBadRequest()
    {
        var result = await _controller.GetTimeline(null, null, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetTimeline_EmptyCorrelationId_ReturnsBadRequest()
    {
        var result = await _controller.GetTimeline(string.Empty, null, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task GetTimeline_WhitespaceCorrelationId_ReturnsBadRequest()
    {
        var result = await _controller.GetTimeline("   ", null, CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    // ─── Namespace resolution ─────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_GetAllNamespacesFails_ReturnsErrorResult()
    {
        _namespaceRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Failure(
                Error.Internal("DB_ERROR", "Database unavailable")));

        var result = await _controller.GetTimeline("corr-id", null, CancellationToken.None);

        result.Result.Should().NotBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task GetTimeline_NoNamespaceId_SearchesAllNamespaces()
    {
        var ns1 = CreateTestNamespace("ns-one-long");
        var ns2 = CreateTestNamespace("ns-two-long");
        var namespaces = new List<Namespace> { ns1, ns2 };

        _namespaceRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(namespaces));

        SetupDefaultWrapper(ns1);
        _clientCache.Setup(c => c.GetOrCreate(ns2.Id, It.IsAny<string>()))
            .Returns(_wrapper.Object);

        var result = await _controller.GetTimeline("corr-id", null, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CorrelationTimelineResponse>().Subject;
        response.NamespacesSearched.Should().Be(2);
    }

    [Fact]
    public async Task GetTimeline_WithNamespaceId_SearchesOnlyThatNamespace()
    {
        var ns = CreateTestNamespace();

        _namespaceRepository.Setup(r => r.GetByIdAsync(ns.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        SetupDefaultWrapper(ns);

        var result = await _controller.GetTimeline("corr-id", ns.Id, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CorrelationTimelineResponse>().Subject;
        response.NamespacesSearched.Should().Be(1);
        _namespaceRepository.Verify(r => r.GetAllAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Live messages ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_MatchingLiveMessages_ReturnedAsLiveSource()
    {
        const string correlationId = "corr-123";
        var ns = CreateTestNamespace();
        var matchingMsg = CreateTestMessage(correlationId, "msg-match");
        var otherMsg = CreateTestMessage("different-corr", "msg-other");

        _namespaceRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        SetupDefaultWrapper(ns, new List<Message> { matchingMsg, otherMsg });

        var result = await _controller.GetTimeline(correlationId, null, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CorrelationTimelineResponse>().Subject;
        response.Entries.Should().HaveCount(1);
        response.Entries[0].Source.Should().Be("Live");
        response.Entries[0].MessageId.Should().Be("msg-match");
    }

    // ─── DLQ history ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_DlqHistoryRecords_ReturnedAsHistorySource()
    {
        const string correlationId = "corr-456";
        var ns = CreateTestNamespace();

        var dlqMsg = new DlqMessage
        {
            MessageId = "dlq-msg-1",
            SequenceNumber = 42,
            BodyHash = "hash-1",
            NamespaceId = ns.Id,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-2),
            DetectedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
            CorrelationId = correlationId,
            Status = DlqMessageStatus.Active
        };
        _dlqContext.DlqMessages.Add(dlqMsg);
        await _dlqContext.SaveChangesAsync();

        _namespaceRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        SetupDefaultWrapper(ns);

        var result = await _controller.GetTimeline(correlationId, null, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CorrelationTimelineResponse>().Subject;
        response.Entries.Should().HaveCount(1);
        response.Entries[0].Source.Should().Be("History");
        response.Entries[0].MessageId.Should().Be("dlq-msg-1");
    }

    // ─── Deduplication ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_SameMessageIdInLiveAndHistory_LiveWins()
    {
        const string correlationId = "corr-789";
        const string sharedMessageId = "shared-msg-id";
        var ns = CreateTestNamespace();

        var dlqMsg = new DlqMessage
        {
            MessageId = sharedMessageId,
            SequenceNumber = 1,
            BodyHash = "hash-2",
            NamespaceId = ns.Id,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow.AddHours(-2),
            DetectedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
            CorrelationId = correlationId,
            Status = DlqMessageStatus.Active
        };
        _dlqContext.DlqMessages.Add(dlqMsg);
        await _dlqContext.SaveChangesAsync();

        var liveMsg = CreateTestMessage(correlationId, sharedMessageId);

        _namespaceRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        SetupDefaultWrapper(ns, new List<Message> { liveMsg });

        var result = await _controller.GetTimeline(correlationId, null, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CorrelationTimelineResponse>().Subject;
        response.Entries.Should().HaveCount(1);
        response.Entries[0].Source.Should().Be("Live");
    }

    // ─── Resilience ───────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_UnprotectFails_SkipsNamespaceAndReturns200()
    {
        const string correlationId = "corr-skip";
        var ns = CreateTestNamespace();

        _namespaceRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        _connectionStringProtector.Setup(p => p.Unprotect(It.IsAny<string>()))
            .Returns(Result<string>.Failure(Error.Internal("UNPROTECT_FAIL", "Decryption failed")));

        var result = await _controller.GetTimeline(correlationId, null, CancellationToken.None);

        // Must still return 200 — individual namespace failures are non-fatal
        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CorrelationTimelineResponse>().Subject;
        response.Entries.Should().BeEmpty();
    }

    // ─── Empty results ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetTimeline_NoMatchingMessages_ReturnsEmptyEntriesWithIsPartialFalse()
    {
        const string correlationId = "no-match-corr";
        var ns = CreateTestNamespace();

        _namespaceRepository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(new List<Namespace> { ns }));

        SetupDefaultWrapper(ns); // returns no messages

        var result = await _controller.GetTimeline(correlationId, null, CancellationToken.None);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var response = ok.Value.Should().BeOfType<CorrelationTimelineResponse>().Subject;
        response.TotalCount.Should().Be(0);
        response.Entries.Should().BeEmpty();
        response.IsPartialResult.Should().BeFalse();
        response.CorrelationId.Should().Be(correlationId);
    }
}
