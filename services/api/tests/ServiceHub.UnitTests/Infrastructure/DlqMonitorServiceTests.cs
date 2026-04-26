using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class DlqMonitorServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<INamespaceRepository> _repoMock = new();
    private readonly Mock<IServiceBusClientCache> _cacheMock = new();
    private readonly Mock<IConnectionStringProtector> _protectorMock = new();
    private readonly Mock<IForensicEngine> _forensicMock = new();
    private readonly Mock<IServiceBusClientWrapper> _wrapperMock = new();
    private readonly DlqMonitorService _sut;

    private static readonly string ValidConnString =
        "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=TestPolicy;SharedAccessKey=abc123=";

    private readonly Guid _namespaceId = Guid.NewGuid();

    public DlqMonitorServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _sut = new DlqMonitorService(
            _dbContext,
            _repoMock.Object,
            _cacheMock.Object,
            _protectorMock.Object,
            _forensicMock.Object,
            NullLogger<DlqMonitorService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    // ── Constructor ─────────────────────────────────────────────────

    [Fact]
    public void Constructor_NullDbContext_Throws()
    {
        var act = () => new DlqMonitorService(
            null!, _repoMock.Object, _cacheMock.Object,
            _protectorMock.Object, _forensicMock.Object,
            NullLogger<DlqMonitorService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_NullNamespaceRepo_Throws()
    {
        var act = () => new DlqMonitorService(
            _dbContext, null!, _cacheMock.Object,
            _protectorMock.Object, _forensicMock.Object,
            NullLogger<DlqMonitorService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("namespaceRepository");
    }

    [Fact]
    public void Constructor_NullClientCache_Throws()
    {
        var act = () => new DlqMonitorService(
            _dbContext, _repoMock.Object, null!,
            _protectorMock.Object, _forensicMock.Object,
            NullLogger<DlqMonitorService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("clientCache");
    }

    [Fact]
    public void Constructor_NullProtector_Throws()
    {
        var act = () => new DlqMonitorService(
            _dbContext, _repoMock.Object, _cacheMock.Object,
            null!, _forensicMock.Object,
            NullLogger<DlqMonitorService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("protector");
    }

    [Fact]
    public void Constructor_NullForensicEngine_Throws()
    {
        var act = () => new DlqMonitorService(
            _dbContext, _repoMock.Object, _cacheMock.Object,
            _protectorMock.Object, null!,
            NullLogger<DlqMonitorService>.Instance);
        act.Should().Throw<ArgumentNullException>().WithParameterName("forensicEngine");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new DlqMonitorService(
            _dbContext, _repoMock.Object, _cacheMock.Object,
            _protectorMock.Object, _forensicMock.Object,
            null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    // ── Helper ──────────────────────────────────────────────────────

    private Namespace SetupValidNamespace()
    {
        var ns = Namespace.Create("test-ns", "PROTECTED:encrypted-data").Value;
        // Use reflection to set Id to match our known _namespaceId
        typeof(Namespace).GetProperty("Id")!.SetValue(ns, _namespaceId);

        _repoMock.Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _protectorMock.Setup(p => p.Unprotect(ns.ConnectionString!))
            .Returns(Result<string>.Success(ValidConnString));

        _cacheMock.Setup(c => c.GetOrCreate(_namespaceId, ValidConnString))
            .Returns(_wrapperMock.Object);

        return ns;
    }

    private static QueueRuntimePropertiesDto MakeQueue(string name, long dlqCount) =>
        new(name, 100, dlqCount, 0, 0, 0, 1024, "Active",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            false, false, false, true, 1024, 10,
            TimeSpan.FromDays(14), TimeSpan.FromMinutes(1), TimeSpan.MaxValue);

    private static TopicRuntimePropertiesDto MakeTopic(string name) =>
        new(name, 1, 1024, "Active",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            false, false, true, false, 1024,
            TimeSpan.FromDays(14), TimeSpan.MaxValue, TimeSpan.FromMinutes(10));

    private static SubscriptionRuntimePropertiesDto MakeSub(string name, string topic, long dlqCount) =>
        new(name, topic, 100, dlqCount, 0, 0, "Active",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow,
            false, true, true, true, 10,
            TimeSpan.FromDays(14), TimeSpan.FromMinutes(1), TimeSpan.MaxValue,
            null, null);

    private static Message MakeMessage(long sequenceNumber, string? dlqReason = "MaxDeliveryCountExceeded") =>
        new()
        {
            MessageId = Guid.NewGuid().ToString(),
            SequenceNumber = sequenceNumber,
            Body = "test body",
            ContentType = "application/json",
            EnqueuedTime = DateTimeOffset.UtcNow,
            DeadLetterReason = dlqReason,
            DeadLetterErrorDescription = "max delivery",
            DeliveryCount = 10,
            SizeInBytes = 100,
        };

    // ═══════════════════════════════════════════════════════════════
    // ScanNamespaceAsync
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public async Task ScanNamespace_NamespaceNotFound_ReturnsFailure()
    {
        _repoMock.Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Failure(Error.NotFound("ns", "not found")));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ScanNamespace_NullConnectionString_ReturnsFailure()
    {
        // Create namespace with managed identity (null connection string)
        var ns = Namespace.CreateWithManagedIdentity("mi-test-ns").Value;
        typeof(Namespace).GetProperty("Id")!.SetValue(ns, _namespaceId);

        _repoMock.Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ScanNamespace_UnprotectFails_ReturnsFailure()
    {
        var ns = Namespace.Create("test-ns", "PROTECTED:encrypted-data").Value;
        typeof(Namespace).GetProperty("Id")!.SetValue(ns, _namespaceId);

        _repoMock.Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _protectorMock.Setup(p => p.Unprotect(ns.ConnectionString!))
            .Returns(Result<string>.Failure(Error.Internal("err", "decrypt failed")));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ScanNamespace_ClientCacheThrows_ReturnsFailure()
    {
        var ns = Namespace.Create("test-ns", "PROTECTED:encrypted-data").Value;
        typeof(Namespace).GetProperty("Id")!.SetValue(ns, _namespaceId);

        _repoMock.Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        _protectorMock.Setup(p => p.Unprotect(ns.ConnectionString!))
            .Returns(Result<string>.Success(ValidConnString));

        _cacheMock.Setup(c => c.GetOrCreate(_namespaceId, ValidConnString))
            .Throws(new InvalidOperationException("connection failed"));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("Failed to create");
    }

    [Fact]
    public async Task ScanNamespace_NoQueuesNoTopics_ReturnsZero()
    {
        SetupValidNamespace();

        _wrapperMock.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(
                Array.Empty<QueueRuntimePropertiesDto>()));

        _wrapperMock.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(
                Array.Empty<TopicRuntimePropertiesDto>()));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task ScanNamespace_QueueWithDlqMessages_StoresNewMessages()
    {
        SetupValidNamespace();

        var queue = MakeQueue("test-queue", 1);
        _wrapperMock.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(new[] { queue }));

        _wrapperMock.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(
                Array.Empty<TopicRuntimePropertiesDto>()));

        var message = MakeMessage(42);
        _wrapperMock.Setup(w => w.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new[] { message }));

        _forensicMock.Setup(f => f.Analyse(It.IsAny<DlqMessage>()))
            .Returns(new ForensicEngineResult(FailureCategory.MaxDelivery, 0.99, "Max delivery", "Safe", "Deterministic"));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        var stored = await _dbContext.DlqMessages.FirstOrDefaultAsync();
        stored.Should().NotBeNull();
        stored!.EntityName.Should().Be("test-queue");
        stored.SequenceNumber.Should().Be(42);
        stored.FailureCategory.Should().Be(FailureCategory.MaxDelivery);
    }

    [Fact]
    public async Task ScanNamespace_DuplicateMessage_NotStoredAgain()
    {
        SetupValidNamespace();

        // Pre-insert a message
        _dbContext.DlqMessages.Add(new DlqMessage
        {
            MessageId = "existing-msg",
            SequenceNumber = 42,
            BodyHash = "abc",
            NamespaceId = _namespaceId,
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        });
        await _dbContext.SaveChangesAsync();

        var queue = MakeQueue("test-queue", 1);
        _wrapperMock.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(new[] { queue }));

        _wrapperMock.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(
                Array.Empty<TopicRuntimePropertiesDto>()));

        var message = new Message
        {
            MessageId = "existing-msg",
            SequenceNumber = 42,
            Body = "test",
            EnqueuedTime = DateTimeOffset.UtcNow,
            DeliveryCount = 10,
        };
        _wrapperMock.Setup(w => w.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new[] { message }));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0); // No new messages

        var count = await _dbContext.DlqMessages.CountAsync();
        count.Should().Be(1); // Still just the one
    }

    [Fact]
    public async Task ScanNamespace_QueueWithZeroDlq_ReconcilesStaleMesages()
    {
        SetupValidNamespace();

        // Pre-insert an Active message that should be reconciled
        _dbContext.DlqMessages.Add(new DlqMessage
        {
            MessageId = "stale-msg",
            SequenceNumber = 99,
            BodyHash = "abc",
            NamespaceId = _namespaceId,
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "empty-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        });
        await _dbContext.SaveChangesAsync();

        var queue = MakeQueue("empty-queue", 0); // No DLQ messages
        _wrapperMock.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(new[] { queue }));

        _wrapperMock.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(
                Array.Empty<TopicRuntimePropertiesDto>()));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();

        var msg = await _dbContext.DlqMessages.FirstAsync();
        msg.Status.Should().Be(DlqMessageStatus.Replayed);
        msg.ReplayedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ScanNamespace_SubscriptionDlqMessages_StoresWithFullEntityName()
    {
        SetupValidNamespace();

        _wrapperMock.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(
                Array.Empty<QueueRuntimePropertiesDto>()));

        var topic = MakeTopic("orders-topic");
        _wrapperMock.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(new[] { topic }));

        var sub = MakeSub("processor-sub", "orders-topic", 1);
        _wrapperMock.Setup(w => w.GetSubscriptionsAsync("orders-topic", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<SubscriptionRuntimePropertiesDto>>.Success(new[] { sub }));

        var message = MakeMessage(100);
        _wrapperMock.Setup(w => w.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new[] { message }));

        _forensicMock.Setup(f => f.Analyse(It.IsAny<DlqMessage>()))
            .Returns(new ForensicEngineResult(FailureCategory.Transient, 0.93, "Connection refused", "Safe", "Deterministic"));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(1);

        var stored = await _dbContext.DlqMessages.FirstAsync();
        stored.EntityName.Should().Be("orders-topic/subscriptions/processor-sub");
        stored.TopicName.Should().Be("orders-topic");
    }

    [Fact]
    public async Task ScanNamespace_GetQueuesFails_HandlesGracefully()
    {
        SetupValidNamespace();

        _wrapperMock.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Failure(
                Error.ExternalService("err", "timeout")));

        _wrapperMock.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(
                Array.Empty<TopicRuntimePropertiesDto>()));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        // Should still succeed (with 0 new) since errors are caught
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task ScanNamespace_PeekMessagesFails_ReturnsZeroForThatEntity()
    {
        SetupValidNamespace();

        var queue = MakeQueue("test-queue", 5);
        _wrapperMock.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(new[] { queue }));

        _wrapperMock.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(
                Array.Empty<TopicRuntimePropertiesDto>()));

        _wrapperMock.Setup(w => w.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Failure(Error.ExternalService("err", "timeout")));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public async Task ScanNamespace_MessageRemovedFromDlq_MarkedAsReplayed()
    {
        SetupValidNamespace();

        // Pre-insert message with sequence 50 that is no longer in the DLQ
        _dbContext.DlqMessages.Add(new DlqMessage
        {
            MessageId = "removed-msg",
            SequenceNumber = 50,
            BodyHash = "abc",
            NamespaceId = _namespaceId,
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Active,
        });
        await _dbContext.SaveChangesAsync();

        var queue = MakeQueue("test-queue", 1);
        _wrapperMock.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(new[] { queue }));

        _wrapperMock.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(
                Array.Empty<TopicRuntimePropertiesDto>()));

        // Return a DIFFERENT message (seq 60), so seq 50 is "gone"
        var message = MakeMessage(60);
        _wrapperMock.Setup(w => w.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new[] { message }));

        _forensicMock.Setup(f => f.Analyse(It.IsAny<DlqMessage>()))
            .Returns(new ForensicEngineResult(FailureCategory.MaxDelivery, 0.99, "max", "Safe", "Deterministic"));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();

        var removedMsg = await _dbContext.DlqMessages.FirstAsync(m => m.SequenceNumber == 50);
        removedMsg.Status.Should().Be(DlqMessageStatus.Replayed);
        removedMsg.ReplayedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task ScanNamespace_PreviouslyReplayedMessage_ReappearsInDlq_StatusUpdatedToActive()
    {
        SetupValidNamespace();

        // Pre-insert replayed message
        _dbContext.DlqMessages.Add(new DlqMessage
        {
            MessageId = "reappeared-msg",
            SequenceNumber = 75,
            BodyHash = "abc",
            NamespaceId = _namespaceId,
            OwnerId = TestConstants.TestOwnerId,
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = DlqMessageStatus.Replayed,
            ReplayedAt = DateTimeOffset.UtcNow.AddMinutes(-5),
        });
        await _dbContext.SaveChangesAsync();

        var queue = MakeQueue("test-queue", 1);
        _wrapperMock.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(new[] { queue }));

        _wrapperMock.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<TopicRuntimePropertiesDto>>.Success(
                Array.Empty<TopicRuntimePropertiesDto>()));

        // Message reappears in DLQ with same sequence number
        var message = new Message
        {
            MessageId = "reappeared-msg",
            SequenceNumber = 75,
            Body = "test",
            EnqueuedTime = DateTimeOffset.UtcNow,
            DeliveryCount = 10,
        };
        _wrapperMock.Setup(w => w.PeekMessagesAsync(It.IsAny<GetMessagesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Message>>.Success(new[] { message }));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0); // Not counted as "new"

        var msg = await _dbContext.DlqMessages.FirstAsync();
        msg.Status.Should().Be(DlqMessageStatus.Active);
        msg.ReplayedAt.Should().BeNull();
    }

    [Fact]
    public async Task ScanNamespace_GetTopicsThrows_HandlesGracefully()
    {
        SetupValidNamespace();

        _wrapperMock.Setup(w => w.GetQueuesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<QueueRuntimePropertiesDto>>.Success(
                Array.Empty<QueueRuntimePropertiesDto>()));

        _wrapperMock.Setup(w => w.GetTopicsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("topic error"));

        var result = await _sut.ScanNamespaceAsync(_namespaceId);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }
}
