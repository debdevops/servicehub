using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class DlqMonitorServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly Mock<IForensicEngineRouter> _forensicMock = new();
    private readonly Mock<ICloudMessagingProvider> _providerMock = new();
    private readonly Mock<IMessageReceiver> _receiverMock = new();

    private readonly Guid _namespaceId = Guid.NewGuid();

    public DlqMonitorServiceTests()
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

    // ── Helpers ─────────────────────────────────────────────────────

    private DlqMonitorService CreateSut(
        CloudProviderType registeredProvider,
        bool allowDestructivePeek = false,
        DlqNotMonitoredLogGuard? logGuard = null,
        ILogger<DlqMonitorService>? logger = null)
    {
        _providerMock.SetupGet(p => p.ProviderType).Returns(registeredProvider);
        _providerMock.SetupGet(p => p.Capabilities).Returns(registeredProvider switch
        {
            CloudProviderType.Aws => ProviderCapabilities.Aws,
            CloudProviderType.Gcp => ProviderCapabilities.Gcp,
            _ => ProviderCapabilities.Azure,
        });
        _providerMock.Setup(p => p.GetMessageReceiver()).Returns(_receiverMock.Object);
        var router = new CloudProviderRouter(new[] { _providerMock.Object });

        var configData = allowDestructivePeek
            ? new Dictionary<string, string?> { [$"DlqMonitor:AllowDestructivePeek:{registeredProvider}"] = "true" }
            : new Dictionary<string, string?>();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(configData).Build();

        return new DlqMonitorService(
            _dbContext, _repoMock.Object, router, _forensicMock.Object,
            configuration, logGuard ?? new DlqNotMonitoredLogGuard(),
            logger ?? NullLogger<DlqMonitorService>.Instance);
    }

    private Namespace SetupNamespace(CloudProviderType provider)
    {
        var ns = provider switch
        {
            CloudProviderType.Aws => Namespace.Create("aws-ns", "akid:secret", provider: CloudProviderType.Aws).Value,
            CloudProviderType.Gcp => Namespace.Create("gcp-ns", "{\"type\":\"service_account\"}", provider: CloudProviderType.Gcp).Value,
            _ => Namespace.Create("test-ns", "PROTECTED:encrypted-data").Value,
        };
        typeof(Namespace).GetProperty("Id")!.SetValue(ns, _namespaceId);

        _repoMock.Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        return ns;
    }

    private void SetupEntities(params CloudEntity[] entities)
    {
        _providerMock.Setup(p => p.ListEntitiesAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Success(entities));
    }

    private void SetupPeek(params Message[] messages)
    {
        _receiverMock.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(messages));
    }

    /// <summary>Maps each peek's FromSequenceNumber (-1 for the initial, cursor-less peek) to the batch it should return.</summary>
    private void SetupPeekBatches(Dictionary<long, Message[]> batchesByFromSequenceNumber)
    {
        _receiverMock.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .Returns((GetMessagesRequest req, CancellationToken _) =>
                Task.FromResult(Result<IReadOnlyList<Message>>.Success(
                    batchesByFromSequenceNumber.TryGetValue(req.FromSequenceNumber ?? -1, out var msgs)
                        ? msgs
                        : Array.Empty<Message>())));
    }

    private void SetupForensic()
    {
        _forensicMock.Setup(f => f.Analyse(It.IsAny<DlqMessage>()))
            .Returns(new ForensicEngineResult(FailureCategory.MaxDelivery, 0.99, "Max delivery", "Safe", "Deterministic"));
    }

    private static CloudEntity MakeEntity(
        string name, string entityType, long dlqCount, CloudProviderType provider) =>
        new()
        {
            Name = name,
            EntityType = entityType,
            DeadLetterCount = dlqCount,
            Provider = provider,
        };

    private static Message MakeMessage(long sequenceNumber, string? messageId = null) =>
        new()
        {
            MessageId = messageId ?? Guid.NewGuid().ToString(),
            SequenceNumber = sequenceNumber,
            Body = "test body",
            ContentType = "application/json",
            EnqueuedTime = DateTimeOffset.UtcNow,
            DeadLetterReason = "MaxDeliveryCountExceeded",
            DeadLetterErrorDescription = "max delivery",
            DeliveryCount = 10,
            SizeInBytes = 100,
        };

    private DlqMessage MakeStoredMessage(
        string messageId, long sequenceNumber, string entityName,
        CloudProviderType provider = CloudProviderType.Azure,
        DlqMessageStatus status = DlqMessageStatus.Active,
        ServiceBusEntityType entityType = ServiceBusEntityType.Queue) =>
        new()
        {
            MessageId = messageId,
            SequenceNumber = sequenceNumber,
            BodyHash = "abc",
            NamespaceId = _namespaceId,
            OwnerId = TestConstants.TestOwnerId,
            CloudProvider = provider,
            EntityName = entityName,
            EntityType = entityType,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = status,
        };

    // ── Constructor ─────────────────────────────────────────────────

    private static IConfiguration EmptyConfig() => new ConfigurationBuilder().Build();

    [Fact]
    public void Constructor_NullDbContext_Throws()
    {
        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var act = () => new DlqMonitorService(
            null!, _repoMock.Object, router, _forensicMock.Object,
            EmptyConfig(), new DlqNotMonitoredLogGuard(), NullLogger<DlqMonitorService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_NullNamespaceRepo_Throws()
    {
        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var act = () => new DlqMonitorService(
            _dbContext, null!, router, _forensicMock.Object,
            EmptyConfig(), new DlqNotMonitoredLogGuard(), NullLogger<DlqMonitorService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullRouter_Throws()
    {
        var act = () => new DlqMonitorService(
            _dbContext, _repoMock.Object, null!, _forensicMock.Object,
            EmptyConfig(), new DlqNotMonitoredLogGuard(), NullLogger<DlqMonitorService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("router");
    }

    [Fact]
    public void Constructor_NullForensicEngine_Throws()
    {
        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var act = () => new DlqMonitorService(
            _dbContext, _repoMock.Object, router, null!,
            EmptyConfig(), new DlqNotMonitoredLogGuard(), NullLogger<DlqMonitorService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("forensicEngine");
    }

    [Fact]
    public void Constructor_NullConfiguration_Throws()
    {
        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var act = () => new DlqMonitorService(
            _dbContext, _repoMock.Object, router, _forensicMock.Object,
            null!, new DlqNotMonitoredLogGuard(), NullLogger<DlqMonitorService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("configuration");
    }

    [Fact]
    public void Constructor_NullNotMonitoredLogGuard_Throws()
    {
        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var act = () => new DlqMonitorService(
            _dbContext, _repoMock.Object, router, _forensicMock.Object,
            EmptyConfig(), null!, NullLogger<DlqMonitorService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("notMonitoredLogGuard");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var router = new CloudProviderRouter(Array.Empty<ICloudMessagingProvider>());
        var act = () => new DlqMonitorService(
            _dbContext, _repoMock.Object, router, _forensicMock.Object,
            EmptyConfig(), new DlqNotMonitoredLogGuard(), null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── Provider guard ──────────────────────────────────────────────

    [Fact]
    public async Task ScanNamespaceAsync_UnregisteredProvider_ReturnsFailure_WithoutListingEntities()
    {
        var sut = CreateSut(CloudProviderType.Azure); // only Azure registered
        SetupNamespace(CloudProviderType.Aws);

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Dlq.ProviderNotSupported");
        _providerMock.Verify(
            p => p.ListEntitiesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ── SupportsRepeatablePeek guard (AWS) ──────────────────────────

    [Fact]
    public async Task ScanNamespaceAsync_Aws_NotOptedIn_SkipsEntirely_ReturnsNotMonitored()
    {
        var sut = CreateSut(CloudProviderType.Aws); // allowDestructivePeek defaults to false
        SetupNamespace(CloudProviderType.Aws);

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Dlq.NotMonitored");
        _providerMock.Verify(
            p => p.ListEntitiesAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _receiverMock.Verify(
            r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanNamespaceAsync_Aws_OptedIn_ProceedsNormally()
    {
        var sut = CreateSut(CloudProviderType.Aws, allowDestructivePeek: true);
        SetupNamespace(CloudProviderType.Aws);
        SetupEntities(MakeEntity("orders", "Queue", 1, CloudProviderType.Aws));
        SetupPeek(MakeMessage(111, "sqs-msg-1"));
        SetupForensic();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);
        _receiverMock.Verify(
            r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanNamespaceAsync_AzureAndGcp_IgnoreAwsOptInFlag_AlwaysScan()
    {
        // Azure and Gcp both declare SupportsRepeatablePeek: true, so the gate must never apply
        // to them even though the config only ever carries an Aws-scoped key.
        var azureSut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);
        SetupEntities(MakeEntity("test-queue", "Queue", 1, CloudProviderType.Azure));
        SetupPeek(MakeMessage(1));
        SetupForensic();

        var azureResult = await azureSut.ScanNamespaceAsync(_namespaceId);
        azureResult.IsSuccess.Should().BeTrue();

        var gcpSut = CreateSut(CloudProviderType.Gcp);
        SetupNamespace(CloudProviderType.Gcp);
        SetupEntities(MakeEntity("orders-sub", "Subscription", 0, CloudProviderType.Gcp));
        SetupPeek(MakeMessage(2, "pubsub-msg"));
        SetupForensic();

        var gcpResult = await gcpSut.ScanNamespaceAsync(_namespaceId);
        gcpResult.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task ScanNamespaceAsync_Aws_NotOptedIn_LogsWarningOnlyOnce()
    {
        var loggerMock = new Mock<ILogger<DlqMonitorService>>();
        var logGuard = new DlqNotMonitoredLogGuard();
        var sut = CreateSut(CloudProviderType.Aws, logGuard: logGuard, logger: loggerMock.Object);
        SetupNamespace(CloudProviderType.Aws);

        await sut.ScanNamespaceAsync(_namespaceId);
        await sut.ScanNamespaceAsync(_namespaceId);

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    // ═══════════════════════════════════════════════════════════════
    // ScanNamespaceAsync — Azure
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ScanNamespace_NamespaceNotFound_ReturnsFailure()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        _repoMock.Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("ns", "not found")));

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ScanNamespace_ListEntitiesFails_ReturnsFailure()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);

        _providerMock.Setup(p => p.ListEntitiesAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<CloudEntity>>.Failure(
                Error.ExternalService("err", "connection failed")));

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("connection failed");
    }

    [Fact]
    public async Task ScanNamespace_NoEntities_ReturnsZero()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);
        SetupEntities();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task ScanNamespace_QueueWithDlqMessages_StoresNewMessages()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);
        SetupEntities(MakeEntity("test-queue", "Queue", 1, CloudProviderType.Azure));
        SetupPeek(MakeMessage(42));
        SetupForensic();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        var stored = await _dbContext.DlqMessages.FirstOrDefaultAsync();
        stored.Should().NotBeNull();
        stored!.EntityName.Should().Be("test-queue");
        stored.SequenceNumber.Should().Be(42);
        stored.CloudProvider.Should().Be(CloudProviderType.Azure);
        stored.FailureCategory.Should().Be(FailureCategory.MaxDelivery);
    }

    [Fact]
    public async Task ScanNamespace_AzureQueueWithZeroDlq_NotPeeked_ReconcilesStaleMessagesAsResolved()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);

        // Pre-insert an Active message that should be reconciled
        _dbContext.DlqMessages.Add(MakeStoredMessage("stale-msg", 99, "empty-queue"));
        await _dbContext.SaveChangesAsync();

        SetupEntities(MakeEntity("empty-queue", "Queue", 0, CloudProviderType.Azure));

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();

        _receiverMock.Verify(
            r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // An empty DLQ proves absence, not that ServiceHub replayed the message — a background
        // scan must never fabricate "Replayed" (see the critical invariant in DlqMonitorService).
        var msg = await _dbContext.DlqMessages.FirstAsync();
        msg.Status.Should().Be(DlqMessageStatus.Resolved);
        msg.ResolvedAt.Should().NotBeNull();
        msg.ResolutionCause.Should().Be(DlqResolutionCause.VanishedExternally);
    }

    [Fact]
    public async Task ScanNamespace_DuplicateMessage_NotStoredAgain()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);

        _dbContext.DlqMessages.Add(MakeStoredMessage("existing-msg", 42, "test-queue"));
        await _dbContext.SaveChangesAsync();

        SetupEntities(MakeEntity("test-queue", "Queue", 1, CloudProviderType.Azure));
        SetupPeek(MakeMessage(42, "existing-msg"));

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0); // No new messages

        var count = await _dbContext.DlqMessages.CountAsync();
        count.Should().Be(1); // Still just the one
    }

    [Fact]
    public async Task ScanNamespace_SubscriptionDlqMessages_StoresWithFullEntityName()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);
        SetupEntities(MakeEntity(
            "orders-topic/subscriptions/processor-sub", "Subscription", 1, CloudProviderType.Azure));
        SetupPeek(MakeMessage(100));
        SetupForensic();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        _receiverMock.Verify(r => r.PeekDeadLetterMessagesAsync(
            It.Is<GetMessagesRequest>(req =>
                req.EntityName == "orders-topic" && req.SubscriptionName == "processor-sub"),
            It.IsAny<CancellationToken>()));

        var stored = await _dbContext.DlqMessages.FirstAsync();
        stored.EntityName.Should().Be("orders-topic/subscriptions/processor-sub");
        stored.TopicName.Should().Be("orders-topic");
        stored.EntityType.Should().Be(ServiceBusEntityType.Subscription);
    }

    [Fact]
    public async Task ScanNamespace_PeekMessagesFails_ReturnsZeroForThatEntity()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);
        SetupEntities(MakeEntity("test-queue", "Queue", 5, CloudProviderType.Azure));

        _receiverMock.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Failure(Error.ExternalService("err", "timeout")));

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task ScanNamespace_MessageRemovedFromDlq_MarkedAsResolvedVanishedExternally()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);

        // Pre-insert message with sequence 50 that is no longer in the DLQ
        _dbContext.DlqMessages.Add(MakeStoredMessage("removed-msg", 50, "test-queue"));
        await _dbContext.SaveChangesAsync();

        SetupEntities(MakeEntity("test-queue", "Queue", 1, CloudProviderType.Azure));

        // Return a DIFFERENT message (seq 60), so seq 50 is "gone"
        SetupPeek(MakeMessage(60));
        SetupForensic();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();

        // Absence from a peek sample proves the message left the DLQ, not that ServiceHub
        // replayed it — external drain, TTL expiry, and another tool are equally possible, so
        // the cause is recorded as VanishedExternally rather than an unverified "Replayed".
        var removedMsg = await _dbContext.DlqMessages.FirstAsync(m => m.SequenceNumber == 50);
        removedMsg.Status.Should().Be(DlqMessageStatus.Resolved);
        removedMsg.ResolvedAt.Should().NotBeNull();
        removedMsg.ResolutionCause.Should().Be(DlqResolutionCause.VanishedExternally);
    }

    [Fact]
    public async Task ScanNamespace_PreviouslyResolvedMessage_ReappearsInDlq_StatusUpdatedToActive_PriorEvidencePreserved()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);

        var resolved = MakeStoredMessage("reappeared-msg", 75, "test-queue",
            status: DlqMessageStatus.Resolved);
        var priorResolvedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        resolved.ResolvedAt = priorResolvedAt;
        resolved.ResolutionCause = DlqResolutionCause.VanishedExternally;
        _dbContext.DlqMessages.Add(resolved);
        await _dbContext.SaveChangesAsync();

        SetupEntities(MakeEntity("test-queue", "Queue", 1, CloudProviderType.Azure));

        // Message reappears in DLQ with same sequence number
        SetupPeek(MakeMessage(75, "reappeared-msg"));

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0); // Not counted as "new"

        // The prior resolution is real evidence of what was previously recorded — a message
        // returning to the DLQ must not erase it (P3).
        var msg = await _dbContext.DlqMessages.FirstAsync();
        msg.Status.Should().Be(DlqMessageStatus.Active);
        msg.ResolvedAt.Should().Be(priorResolvedAt);
        msg.ResolutionCause.Should().Be(DlqResolutionCause.VanishedExternally);
    }

    // ── C6 regression: paging past the first PeekBatchSize (100) messages ─

    [Fact]
    public async Task ScanNamespace_AzureBacklogBeyondFirstBatch_PagesWithAdvancedSequenceNumber_StoresAll()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);
        SetupEntities(MakeEntity("test-queue", "Queue", 120, CloudProviderType.Azure));

        var firstBatch = Enumerable.Range(1, 100).Select(i => MakeMessage(i)).ToArray();
        var secondBatch = Enumerable.Range(101, 20).Select(i => MakeMessage(i)).ToArray();
        SetupPeekBatches(new Dictionary<long, Message[]>
        {
            [-1L] = firstBatch,
            [101L] = secondBatch,
        });
        SetupForensic();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(120); // all 120, not capped at the first 100

        _receiverMock.Verify(r => r.PeekDeadLetterMessagesAsync(
            It.Is<GetMessagesRequest>(req => req.FromSequenceNumber == null),
            It.IsAny<CancellationToken>()), Times.Once);
        _receiverMock.Verify(r => r.PeekDeadLetterMessagesAsync(
            It.Is<GetMessagesRequest>(req => req.FromSequenceNumber == 101),
            It.IsAny<CancellationToken>()), Times.Once);

        var count = await _dbContext.DlqMessages.CountAsync();
        count.Should().Be(120);
    }

    [Fact]
    public async Task ScanNamespace_AzureUnboundedBacklog_StopsAtSafetyCap()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);
        SetupEntities(MakeEntity("test-queue", "Queue", 100_000, CloudProviderType.Azure));
        SetupForensic();

        var callCount = 0;
        _receiverMock.Setup(r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .Returns((GetMessagesRequest req, CancellationToken _) =>
            {
                callCount++;
                var start = req.FromSequenceNumber ?? 1;
                var batch = Enumerable.Range(0, 100).Select(i => MakeMessage(start + i)).ToArray();
                return Task.FromResult(Result<IReadOnlyList<Message>>.Success((IReadOnlyList<Message>)batch));
            });

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        callCount.Should().Be(50); // MaxScanBatchesPerEntity — always-full-batch backlog never self-terminates
        result.Value.Should().Be(5000);
    }

    [Fact]
    public async Task ScanNamespace_AwsFullBatch_DoesNotPageFurther_NoStableSequenceCursor()
    {
        // AWS/GCP sequence numbers are unstable per-peek hashes (see ScanEntityDlqAsync's
        // useSequenceKey comment), so pagination must not be attempted for them even when a
        // batch comes back full — unlike Azure, this must remain a single peek per scan.
        var sut = CreateSut(CloudProviderType.Aws, allowDestructivePeek: true);
        SetupNamespace(CloudProviderType.Aws);
        SetupEntities(MakeEntity("orders", "Queue", 100, CloudProviderType.Aws));

        var fullBatch = Enumerable.Range(1, 100).Select(i => MakeMessage(i, $"sqs-msg-{i}")).ToArray();
        SetupPeek(fullBatch);
        SetupForensic();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(100);

        _receiverMock.Verify(
            r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ScanNamespace_TopicEntity_NotScanned()
    {
        var sut = CreateSut(CloudProviderType.Azure);
        SetupNamespace(CloudProviderType.Azure);
        SetupEntities(MakeEntity("orders-topic", "Topic", 0, CloudProviderType.Azure));

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        _receiverMock.Verify(
            r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ═══════════════════════════════════════════════════════════════
    // ScanNamespaceAsync — AWS
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ScanNamespace_AwsQueueWithZeroDlqCount_NotPeeked_ReconcilesStaleMessagesAsResolved()
    {
        var sut = CreateSut(CloudProviderType.Aws, allowDestructivePeek: true);
        SetupNamespace(CloudProviderType.Aws);

        // AWS ListEntitiesAsync reliably populates DeadLetterCount (via the redrive-target
        // queue's ApproximateNumberOfMessages — see AwsMessagingProvider.ListEntitiesAsync),
        // same as Azure. A genuine 0 is trusted and skips the peek entirely.
        _dbContext.DlqMessages.Add(MakeStoredMessage("stale-msg", 99, "orders", CloudProviderType.Aws));
        await _dbContext.SaveChangesAsync();

        SetupEntities(MakeEntity("orders", "Queue", 0, CloudProviderType.Aws));

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();

        _receiverMock.Verify(
            r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);

        var msg = await _dbContext.DlqMessages.FirstAsync();
        msg.Status.Should().Be(DlqMessageStatus.Resolved);
        msg.ResolvedAt.Should().NotBeNull();
        msg.ResolutionCause.Should().Be(DlqResolutionCause.VanishedExternally);
    }

    [Fact]
    public async Task ScanNamespace_AwsQueueWithPositiveDlqCount_Peeked_StoresWithAwsProvider()
    {
        var sut = CreateSut(CloudProviderType.Aws, allowDestructivePeek: true);
        SetupNamespace(CloudProviderType.Aws);

        SetupEntities(MakeEntity("orders", "Queue", 1, CloudProviderType.Aws));
        SetupPeek(MakeMessage(111, "sqs-msg-1"));
        SetupForensic();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        var stored = await _dbContext.DlqMessages.FirstAsync();
        stored.EntityName.Should().Be("orders");
        stored.MessageId.Should().Be("sqs-msg-1");
        stored.CloudProvider.Should().Be(CloudProviderType.Aws);
        stored.EntityType.Should().Be(ServiceBusEntityType.Queue);
    }

    [Fact]
    public async Task ScanNamespace_AwsSameMessageIdDifferentSequenceNumber_NotDuplicated()
    {
        var sut = CreateSut(CloudProviderType.Aws, allowDestructivePeek: true);
        SetupNamespace(CloudProviderType.Aws);

        // AWS sequence numbers are hashes of per-delivery receipt handles and change every scan.
        _dbContext.DlqMessages.Add(MakeStoredMessage("sqs-msg-1", 111, "orders", CloudProviderType.Aws));
        await _dbContext.SaveChangesAsync();

        SetupEntities(MakeEntity("orders", "Queue", 1, CloudProviderType.Aws));
        SetupPeek(MakeMessage(222, "sqs-msg-1")); // same MessageId, new sequence number

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0); // No new messages

        var count = await _dbContext.DlqMessages.CountAsync();
        count.Should().Be(1);
        var msg = await _dbContext.DlqMessages.FirstAsync();
        msg.Status.Should().Be(DlqMessageStatus.Active);
    }

    [Fact]
    public async Task ScanNamespace_AwsMessageGone_ReconciledByMessageId_MarkedResolved()
    {
        var sut = CreateSut(CloudProviderType.Aws, allowDestructivePeek: true);
        SetupNamespace(CloudProviderType.Aws);

        _dbContext.DlqMessages.Add(MakeStoredMessage("gone-msg", 111, "orders", CloudProviderType.Aws));
        await _dbContext.SaveChangesAsync();

        SetupEntities(MakeEntity("orders", "Queue", 1, CloudProviderType.Aws));
        SetupPeek(MakeMessage(222, "other-msg"));
        SetupForensic();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();

        var goneMsg = await _dbContext.DlqMessages.FirstAsync(m => m.MessageId == "gone-msg");
        goneMsg.Status.Should().Be(DlqMessageStatus.Resolved);
        goneMsg.ResolvedAt.Should().NotBeNull();
        goneMsg.ResolutionCause.Should().Be(DlqResolutionCause.VanishedExternally);
    }

    [Fact]
    public async Task ScanNamespace_AwsFullBatchAtCap_DoesNotFalselyReconcileGoneMessage()
    {
        // A single AWS/GCP peek is capped at PeekBatchSize (100) with no stable pagination
        // cursor, so a full batch does not prove the live DLQ has nothing beyond it. Reconciling
        // against that possibly-truncated sample would falsely mark a still-present message
        // (simply not sampled this cycle) as Replayed.
        var sut = CreateSut(CloudProviderType.Aws, allowDestructivePeek: true);
        SetupNamespace(CloudProviderType.Aws);

        _dbContext.DlqMessages.Add(MakeStoredMessage("gone-msg", 111, "orders", CloudProviderType.Aws));
        await _dbContext.SaveChangesAsync();

        SetupEntities(MakeEntity("orders", "Queue", 100, CloudProviderType.Aws));
        var fullBatch = Enumerable.Range(1, 100).Select(i => MakeMessage(i, $"sqs-msg-{i}")).ToArray();
        SetupPeek(fullBatch);
        SetupForensic();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(100);

        var goneMsg = await _dbContext.DlqMessages.FirstAsync(m => m.MessageId == "gone-msg");
        goneMsg.Status.Should().Be(DlqMessageStatus.Active);
        goneMsg.ReplayedAt.Should().BeNull();
    }

    [Fact]
    public async Task ScanNamespace_AwsSnsTopicEntity_NotScanned()
    {
        var sut = CreateSut(CloudProviderType.Aws, allowDestructivePeek: true);
        SetupNamespace(CloudProviderType.Aws);
        SetupEntities(MakeEntity(
            "arn:aws:sns:us-east-1:123456789012:orders", "SNS Topic", 0, CloudProviderType.Aws));

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
        _receiverMock.Verify(
            r => r.PeekDeadLetterMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ScanNamespace_AwsSnsSubscriptionWithZeroDlqCount_ReconcilesStaleMessage_KeyMatchesStoredFormat()
    {
        // P9 regression: AWS/GCP ListEntitiesAsync reports subscriptions as "topic/sub", but
        // DlqMessage.EntityName always persists the normalized "topic/subscriptions/sub" form
        // (the same format ParseEntity/ScanEntityDlqAsync use for every provider). Before the
        // fix, the namespace-level reconcile keyed its lookup on the raw "topic/sub" name and
        // so never matched a stored row, meaning this entity's stale messages were never
        // reconciled at all.
        var sut = CreateSut(CloudProviderType.Aws, allowDestructivePeek: true);
        SetupNamespace(CloudProviderType.Aws);

        _dbContext.DlqMessages.Add(MakeStoredMessage(
            "stale-sns-msg", 99, "orders-topic/subscriptions/order-processor", CloudProviderType.Aws,
            entityType: ServiceBusEntityType.Subscription));
        await _dbContext.SaveChangesAsync();

        // AWS SNS subscriptions are listed as "topic/sub", not "topic/subscriptions/sub".
        SetupEntities(MakeEntity("orders-topic/order-processor", "Subscription", 0, CloudProviderType.Aws));

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();

        var msg = await _dbContext.DlqMessages.FirstAsync();
        msg.Status.Should().Be(DlqMessageStatus.Resolved);
        msg.ResolutionCause.Should().Be(DlqResolutionCause.VanishedExternally);
    }

    // ═══════════════════════════════════════════════════════════════
    // ScanNamespaceAsync — GCP
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ScanNamespace_GcpSubscription_Scanned_StoresWithGcpProvider()
    {
        var sut = CreateSut(CloudProviderType.Gcp);
        SetupNamespace(CloudProviderType.Gcp);

        // GCP subscriptions are listed by bare subscription ID (no "/subscriptions/" path).
        SetupEntities(MakeEntity("orders-sub", "Subscription", 0, CloudProviderType.Gcp));
        SetupPeek(MakeMessage(333, "pubsub-msg-1"));
        SetupForensic();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        _receiverMock.Verify(r => r.PeekDeadLetterMessagesAsync(
            It.Is<GetMessagesRequest>(req => req.EntityName == "orders-sub"),
            It.IsAny<CancellationToken>()));

        var stored = await _dbContext.DlqMessages.FirstAsync();
        stored.EntityName.Should().Be("orders-sub");
        stored.TopicName.Should().BeNull();
        stored.EntityType.Should().Be(ServiceBusEntityType.Subscription);
        stored.CloudProvider.Should().Be(CloudProviderType.Gcp);
    }

    [Fact]
    public async Task ScanNamespace_GcpDlqSubscription_Skipped()
    {
        var sut = CreateSut(CloudProviderType.Gcp);
        SetupNamespace(CloudProviderType.Gcp);

        // "orders-sub-dlq" is the dead-letter subscription itself and must not be scanned.
        SetupEntities(
            MakeEntity("orders-sub", "Subscription", 0, CloudProviderType.Gcp),
            MakeEntity("orders-sub-dlq", "Subscription", 0, CloudProviderType.Gcp));
        SetupPeek(MakeMessage(333, "pubsub-msg-1"));
        SetupForensic();

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();

        _receiverMock.Verify(r => r.PeekDeadLetterMessagesAsync(
            It.Is<GetMessagesRequest>(req => req.EntityName == "orders-sub"),
            It.IsAny<CancellationToken>()), Times.Once);
        _receiverMock.Verify(r => r.PeekDeadLetterMessagesAsync(
            It.IsAny<GetMessagesRequest>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ScanNamespace_GcpSameMessageIdDifferentSequenceNumber_NotDuplicated()
    {
        var sut = CreateSut(CloudProviderType.Gcp);
        SetupNamespace(CloudProviderType.Gcp);

        // GCP sequence numbers are hashes of per-delivery ack IDs and change every scan.
        _dbContext.DlqMessages.Add(MakeStoredMessage("pubsub-msg-1", 333, "orders-sub",
            CloudProviderType.Gcp, entityType: ServiceBusEntityType.Subscription));
        await _dbContext.SaveChangesAsync();

        SetupEntities(MakeEntity("orders-sub", "Subscription", 0, CloudProviderType.Gcp));
        SetupPeek(MakeMessage(444, "pubsub-msg-1")); // same MessageId, new sequence number

        var result = await sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);

        var count = await _dbContext.DlqMessages.CountAsync();
        count.Should().Be(1);
    }
}
