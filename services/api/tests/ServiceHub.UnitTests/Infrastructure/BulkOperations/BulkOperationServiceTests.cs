using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.BulkOperations;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BulkOperations;

public sealed class BulkOperationServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly Mock<IBulkOperationQueue> _queueMock = new();
    private readonly Guid _namespaceId = Guid.NewGuid();
    private const string OwnerId = "entra:test-owner-123";

    public BulkOperationServiceTests()
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

    private BulkOperationService CreateSut(params ICloudMessagingProvider[] providers)
    {
        var router = new CloudProviderRouter(providers);
        return new BulkOperationService(
            _dbContext, _namespaceRepositoryMock.Object, router, _queueMock.Object,
            NullLogger<BulkOperationService>.Instance);
    }

    private static Mock<ICloudMessagingProvider> BuildProviderMock(CloudProviderType type, ProviderCapabilities capabilities)
    {
        var mock = new Mock<ICloudMessagingProvider>();
        mock.SetupGet(p => p.ProviderType).Returns(type);
        mock.SetupGet(p => p.Capabilities).Returns(capabilities);
        return mock;
    }

    private Namespace SetupNamespace(
        CloudProviderType provider = CloudProviderType.Aws,
        EnvironmentType environment = EnvironmentType.Dev,
        string ownerId = OwnerId)
    {
        var ns = provider switch
        {
            CloudProviderType.Aws => Namespace.Create("aws-ns", "akid:secret", environment: environment, provider: provider, ownerId: ownerId).Value,
            CloudProviderType.Gcp => Namespace.Create("gcp-ns", "{\"type\":\"service_account\"}", environment: environment, provider: provider, ownerId: ownerId).Value,
            _ => Namespace.Create("azure-ns", "Endpoint=sb://x/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=abc=", environment: environment, provider: provider, ownerId: ownerId).Value,
        };
        typeof(Namespace).GetProperty(nameof(Namespace.Id))!.SetValue(ns, _namespaceId);

        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        return ns;
    }

    private DlqMessage AddDlqMessage(
        long seq = 1,
        DlqMessageStatus status = DlqMessageStatus.Active,
        string? replaySafety = "Safe",
        string entityName = "orders",
        string ownerId = OwnerId)
    {
        var msg = new DlqMessage
        {
            MessageId = $"msg-{seq}",
            SequenceNumber = seq,
            BodyHash = $"hash-{seq}",
            NamespaceId = _namespaceId,
            OwnerId = ownerId,
            EntityName = entityName,
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            Status = status,
            ReplaySafety = replaySafety,
        };
        _dbContext.DlqMessages.Add(msg);
        _dbContext.SaveChanges();
        return msg;
    }

    private static BulkOperationFilterRequest Filter(Guid namespaceId, DlqMessageStatus? status = DlqMessageStatus.Active) =>
        new(namespaceId, EntityName: null, From: null, To: null, Status: status, Category: null);

    // ── PreviewAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_MatchingMessages_ReturnsCountAndSample()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);
        AddDlqMessage(1);
        AddDlqMessage(2);

        var result = await sut.PreviewAsync(OwnerId,
            new BulkOperationPreviewRequest(BulkOperationType.Replay, Filter(_namespaceId)));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMatched.Should().Be(2);
        result.Value.Sample.Should().HaveCount(2);
        result.Value.CanExecute.Should().BeTrue();
    }

    [Fact]
    public async Task PreviewAsync_NoMatches_CanExecuteIsFalse()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);

        var result = await sut.PreviewAsync(OwnerId,
            new BulkOperationPreviewRequest(BulkOperationType.Replay, Filter(_namespaceId)));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMatched.Should().Be(0);
        result.Value.CanExecute.Should().BeFalse();
        result.Value.Warnings.Should().Contain(w => w.Contains("No DLQ messages match"));
    }

    [Fact]
    public async Task PreviewAsync_ProductionNamespace_CanExecuteIsFalseWithWarning()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws, EnvironmentType.Prod);
        AddDlqMessage(1);

        var result = await sut.PreviewAsync(OwnerId,
            new BulkOperationPreviewRequest(BulkOperationType.Replay, Filter(_namespaceId)));

        result.IsSuccess.Should().BeTrue();
        result.Value.CanExecute.Should().BeFalse();
        result.Value.Warnings.Should().Contain(w => w.Contains("Production"));
    }

    [Fact]
    public async Task PreviewAsync_PurgeOnAzure_CanExecuteIsFalse_CapabilityNotSupported()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Azure, ProviderCapabilities.Azure);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Azure);
        AddDlqMessage(1);

        var result = await sut.PreviewAsync(OwnerId,
            new BulkOperationPreviewRequest(BulkOperationType.Purge, Filter(_namespaceId)));

        result.IsSuccess.Should().BeTrue();
        result.Value.CanExecute.Should().BeFalse();
        result.Value.Warnings.Should().Contain(w => w.Contains("does not support purge"));
    }

    [Fact]
    public async Task PreviewAsync_UnsafeReplayMessages_AreCountedAndWarned()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);
        AddDlqMessage(1, replaySafety: "Unsafe");
        AddDlqMessage(2, replaySafety: "Safe");

        var result = await sut.PreviewAsync(OwnerId,
            new BulkOperationPreviewRequest(BulkOperationType.Replay, Filter(_namespaceId)));

        result.Value.UnsafeReplayCount.Should().Be(1);
        result.Value.Warnings.Should().Contain(w => w.Contains("flagged 'Unsafe'"));
    }

    [Fact]
    public async Task PreviewAsync_UnregisteredProvider_CanExecuteIsFalse()
    {
        var sut = CreateSut(); // no providers registered
        SetupNamespace(CloudProviderType.Aws);
        AddDlqMessage(1);

        var result = await sut.PreviewAsync(OwnerId,
            new BulkOperationPreviewRequest(BulkOperationType.Replay, Filter(_namespaceId)));

        result.Value.CanExecute.Should().BeFalse();
        result.Value.Warnings.Should().Contain(w => w.Contains("No provider is registered"));
    }

    [Fact]
    public async Task PreviewAsync_NamespaceOwnedByAnotherOwner_ReturnsNotFound()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws, ownerId: "entra:someone-else");

        var result = await sut.PreviewAsync(OwnerId,
            new BulkOperationPreviewRequest(BulkOperationType.Replay, Filter(_namespaceId)));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    [Fact]
    public async Task PreviewAsync_NamespaceOutsideAllowList_ReturnsNotFound()
    {
        // A key restricted to a different namespace must not be able to preview a bulk operation
        // against a namespace it truly owns but that's outside its allow-list — otherwise the
        // namespace allow-list would restrict reads while leaving destructive-adjacent operations
        // unrestricted.
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);
        AddDlqMessage(1);

        var result = await sut.PreviewAsync(OwnerId,
            new BulkOperationPreviewRequest(BulkOperationType.Replay, Filter(_namespaceId)),
            allowedNamespaceIds: new HashSet<Guid> { Guid.NewGuid() });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    [Fact]
    public async Task PreviewAsync_NamespaceInAllowList_Succeeds()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);
        AddDlqMessage(1);

        var result = await sut.PreviewAsync(OwnerId,
            new BulkOperationPreviewRequest(BulkOperationType.Replay, Filter(_namespaceId)),
            allowedNamespaceIds: new HashSet<Guid> { _namespaceId });

        result.IsSuccess.Should().BeTrue();
    }

    // ── CreateJobAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateJobAsync_ValidRequest_PersistsPendingJobAndEnqueues()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);
        AddDlqMessage(1);
        AddDlqMessage(2);

        var result = await sut.CreateJobAsync(OwnerId,
            new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId)), "corr-1");

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(nameof(BulkOperationStatus.Pending));
        result.Value.TotalMatched.Should().Be(2);
        result.Value.OperationType.Should().Be(nameof(BulkOperationType.Replay));

        var stored = await _dbContext.BulkOperationJobs.FirstAsync();
        stored.Status.Should().Be(BulkOperationStatus.Pending);
        stored.TotalMatched.Should().Be(2);

        _queueMock.Verify(q => q.Enqueue(result.Value.Id), Times.Once);
    }

    [Fact]
    public async Task CreateJobAsync_ProductionNamespace_FailsAndDoesNotEnqueue()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws, EnvironmentType.Prod);
        AddDlqMessage(1);

        var result = await sut.CreateJobAsync(OwnerId,
            new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId)), null);

        result.IsFailure.Should().BeTrue();
        (await _dbContext.BulkOperationJobs.CountAsync()).Should().Be(0);
        _queueMock.Verify(q => q.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CreateJobAsync_NoMatchingMessages_Fails()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);

        var result = await sut.CreateJobAsync(OwnerId,
            new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId)), null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BulkOperation.NoMatches");
    }

    [Fact]
    public async Task CreateJobAsync_NamespaceOutsideAllowList_ReturnsNotFoundAndDoesNotEnqueue()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);
        AddDlqMessage(1);

        var result = await sut.CreateJobAsync(OwnerId,
            new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId)), null,
            allowedNamespaceIds: new HashSet<Guid> { Guid.NewGuid() });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
        (await _dbContext.BulkOperationJobs.CountAsync()).Should().Be(0);
        _queueMock.Verify(q => q.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CreateJobAsync_PurgeUnsupportedByProvider_Fails()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Azure, ProviderCapabilities.Azure);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Azure);
        AddDlqMessage(1);

        var result = await sut.CreateJobAsync(OwnerId,
            new BulkOperationCreateRequest(BulkOperationType.Purge, Filter(_namespaceId)), null);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("BulkOperation.NotAllowed");
        _queueMock.Verify(q => q.Enqueue(It.IsAny<Guid>()), Times.Never);
    }

    // ── GetJobAsync / ListJobsAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetJobAsync_ExistingJob_ReturnsIt()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);
        AddDlqMessage(1);
        var created = await sut.CreateJobAsync(OwnerId,
            new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId)), null);

        var result = await sut.GetJobAsync(OwnerId, created.Value.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Id.Should().Be(created.Value.Id);
    }

    [Fact]
    public async Task GetJobAsync_UnknownJob_ReturnsNotFound()
    {
        var sut = CreateSut();

        var result = await sut.GetJobAsync(OwnerId, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task GetJobAsync_JobOwnedByAnotherOwner_ReturnsNotFound()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);
        AddDlqMessage(1);
        var created = await sut.CreateJobAsync(OwnerId,
            new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId)), null);

        var result = await sut.GetJobAsync("entra:someone-else", created.Value.Id);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ListJobsAsync_ReturnsMostRecentFirst()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);
        AddDlqMessage(1);
        AddDlqMessage(2);

        var first = await sut.CreateJobAsync(OwnerId,
            new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId)), null);
        await Task.Delay(5); // ensure distinct CreatedAt ordering
        var second = await sut.CreateJobAsync(OwnerId,
            new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId)), null);

        var result = await sut.ListJobsAsync(OwnerId, namespaceId: null, page: 1, pageSize: 20);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items[0].Id.Should().Be(second.Value.Id);
        result.Value.Items[1].Id.Should().Be(first.Value.Id);
    }

    // ── CancelJobAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task CancelJobAsync_PendingJob_SetsCancellationRequestedAndSignalsQueue()
    {
        var providerMock = BuildProviderMock(CloudProviderType.Aws, ProviderCapabilities.Aws);
        var sut = CreateSut(providerMock.Object);
        SetupNamespace(CloudProviderType.Aws);
        AddDlqMessage(1);
        var created = await sut.CreateJobAsync(OwnerId,
            new BulkOperationCreateRequest(BulkOperationType.Replay, Filter(_namespaceId)), null);

        var result = await sut.CancelJobAsync(OwnerId, created.Value.Id);

        result.IsSuccess.Should().BeTrue();
        var stored = await _dbContext.BulkOperationJobs.FirstAsync(j => j.Id == created.Value.Id);
        stored.CancellationRequestedAt.Should().NotBeNull();
        _queueMock.Verify(q => q.RequestCancellation(created.Value.Id), Times.Once);
    }

    [Fact]
    public async Task CancelJobAsync_AlreadyCompletedJob_IsIdempotentNoOp()
    {
        var sut = CreateSut();
        var job = new BulkOperationJob
        {
            OwnerId = OwnerId,
            OperationType = BulkOperationType.Replay,
            Status = BulkOperationStatus.Completed,
            NamespaceId = _namespaceId,
            NamespaceDisplayName = "ns",
            TotalMatched = 1,
            CreatedAt = DateTimeOffset.UtcNow,
            CompletedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.BulkOperationJobs.Add(job);
        await _dbContext.SaveChangesAsync();

        var result = await sut.CancelJobAsync(OwnerId, job.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.Status.Should().Be(nameof(BulkOperationStatus.Completed));
        _queueMock.Verify(q => q.RequestCancellation(It.IsAny<Guid>()), Times.Never);
    }

    [Fact]
    public async Task CancelJobAsync_UnknownJob_ReturnsNotFound()
    {
        var sut = CreateSut();

        var result = await sut.CancelJobAsync(OwnerId, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
    }
}
