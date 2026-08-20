using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Infrastructure.SignatureReplay;
using ServiceHub.Shared.Helpers;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.SignatureReplay;

public sealed class SignatureReplayServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly Mock<IDlqSignatureAnalysisService> _analysisServiceMock = new();
    private readonly Mock<IDlqHistoryService> _historyServiceMock = new();
    private readonly Mock<ISignatureReplayQueue> _queueMock = new();
    private readonly Guid _namespaceId = Guid.NewGuid();
    private const string OwnerId = "entra:test-owner-123";
    private static readonly RecoveryActor TestActor = new(OwnerId, RecoveryActorKind.User);
    private static readonly string[] TopTerms = ["timeout", "connection"];
    private const string DominantReason = "MaxDeliveryCountExceeded";
    private static readonly string SignatureHash = ClusterSignatureHasher.ComputeHash(TopTerms, DominantReason);

    public SignatureReplayServiceTests()
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

    private SignatureReplayService CreateSut(params ICloudMessagingProvider[] providers)
    {
        var router = new CloudProviderRouter(providers);
        return new SignatureReplayService(
            _dbContext, _namespaceRepositoryMock.Object, router, _analysisServiceMock.Object, _historyServiceMock.Object,
            _queueMock.Object, NullLogger<SignatureReplayService>.Instance);
    }

    private static Mock<ICloudMessagingProvider> BuildProviderMock(CloudProviderType type) =>
        new Mock<ICloudMessagingProvider>().Also(m =>
        {
            m.SetupGet(p => p.ProviderType).Returns(type);
            m.SetupGet(p => p.Capabilities).Returns(ProviderCapabilities.Aws);
        });

    private Namespace SetupNamespace(
        EnvironmentType environment = EnvironmentType.Dev, bool hasSendPermission = true, string ownerId = OwnerId)
    {
        var ns = Namespace.Create("aws-ns", "akid:secret", environment: environment,
            provider: CloudProviderType.Aws, ownerId: ownerId).Value;
        typeof(Namespace).GetProperty(nameof(Namespace.Id))!.SetValue(ns, _namespaceId);
        typeof(Namespace).GetProperty(nameof(Namespace.HasSendPermission))!.SetValue(ns, hasSendPermission);

        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(_namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<Namespace>.Success(ns));

        return ns;
    }

    private void SetupSignatureWithMessages(params DlqMessage[] messages)
    {
        var cluster = new DlqClusterSignature(
            Size: messages.Length,
            MessageIds: messages.Select(m => m.Id).ToList(),
            DominantEntity: "orders",
            DominantDeadletterReason: DominantReason,
            DominantDeadletterReasonCount: messages.Length,
            TopTerms: TopTerms,
            IsNew: false,
            FirstSeenAt: DateTimeOffset.UtcNow.AddDays(-1),
            OccurrenceCount: messages.Length,
            WindowStart: DateTimeOffset.UtcNow.AddDays(-1),
            WindowEnd: DateTimeOffset.UtcNow,
            Explanation: "test");

        _analysisServiceMock
            .Setup(s => s.AnalyzeAsync(OwnerId, _namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSignatureAnalysisResult>.Success(
                new DlqSignatureAnalysisResult(true, "clustered", 1, [cluster], [])));

        _historyServiceMock
            .Setup(s => s.GetByIdsAsync(OwnerId, It.IsAny<IReadOnlyList<long>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<DlqMessage>>.Success(messages));
    }

    private void SetupNoClusteredSignature()
    {
        _analysisServiceMock
            .Setup(s => s.AnalyzeAsync(OwnerId, _namespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<DlqSignatureAnalysisResult>.Success(
                new DlqSignatureAnalysisResult(true, "clustered", 1, [], [])));
    }

    private DlqMessage BuildMessage(
        long id, DlqMessageStatus status = DlqMessageStatus.Active, string? replaySafety = "Safe",
        DateTimeOffset? detectedAtUtc = null)
    {
        var msg = new DlqMessage
        {
            MessageId = $"msg-{id}",
            SequenceNumber = id,
            BodyHash = $"hash-{id}",
            NamespaceId = _namespaceId,
            OwnerId = OwnerId,
            EntityName = "orders",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = detectedAtUtc ?? DateTimeOffset.UtcNow,
            Status = status,
            ReplaySafety = replaySafety,
        };
        typeof(DlqMessage).GetProperty(nameof(DlqMessage.Id))!.SetValue(msg, id);
        return msg;
    }

    private static SignatureReplayFilterRequest Filter(
        Guid namespaceId, DlqMessageStatus? status = null, DateTimeOffset? from = null, DateTimeOffset? to = null) =>
        new(namespaceId, SignatureHash, status, from, to);

    // ── PreviewAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task PreviewAsync_MatchingMessages_ReturnsCountAndSample()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace();
        SetupSignatureWithMessages(BuildMessage(1), BuildMessage(2));

        var result = await sut.PreviewAsync(OwnerId, new SignatureReplayPreviewRequest(Filter(_namespaceId)));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMatched.Should().Be(2);
        result.Value.CanExecute.Should().BeTrue();
    }

    [Fact]
    public async Task PreviewAsync_SignatureNotCurrentlyClustered_ReturnsZeroMatches()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace();
        SetupNoClusteredSignature();

        var result = await sut.PreviewAsync(OwnerId, new SignatureReplayPreviewRequest(Filter(_namespaceId)));

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMatched.Should().Be(0);
        result.Value.CanExecute.Should().BeFalse();
    }

    [Fact]
    public async Task PreviewAsync_UnresolvedOnlyFilter_ExcludesReplayedMessages()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace();
        SetupSignatureWithMessages(
            BuildMessage(1, status: DlqMessageStatus.Active),
            BuildMessage(2, status: DlqMessageStatus.Replayed));

        var result = await sut.PreviewAsync(OwnerId,
            new SignatureReplayPreviewRequest(Filter(_namespaceId, status: DlqMessageStatus.Active)));

        result.Value.TotalMatched.Should().Be(1);
    }

    [Fact]
    public async Task PreviewAsync_FailedReplayOnlyFilter_MatchesOnlyReplayFailed()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace();
        SetupSignatureWithMessages(
            BuildMessage(1, status: DlqMessageStatus.Active),
            BuildMessage(2, status: DlqMessageStatus.ReplayFailed));

        var result = await sut.PreviewAsync(OwnerId,
            new SignatureReplayPreviewRequest(Filter(_namespaceId, status: DlqMessageStatus.ReplayFailed)));

        result.Value.TotalMatched.Should().Be(1);
    }

    [Fact]
    public async Task PreviewAsync_DateRangeFilter_ExcludesOutOfRangeMessages()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace();
        var now = DateTimeOffset.UtcNow;
        SetupSignatureWithMessages(
            BuildMessage(1, detectedAtUtc: now.AddDays(-10)),
            BuildMessage(2, detectedAtUtc: now));

        var result = await sut.PreviewAsync(OwnerId,
            new SignatureReplayPreviewRequest(Filter(_namespaceId, from: now.AddDays(-1))));

        result.Value.TotalMatched.Should().Be(1);
    }

    [Fact]
    public async Task PreviewAsync_ProductionNamespace_CanExecuteIsFalse()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace(EnvironmentType.Prod);
        SetupSignatureWithMessages(BuildMessage(1));

        var result = await sut.PreviewAsync(OwnerId, new SignatureReplayPreviewRequest(Filter(_namespaceId)));

        result.Value.CanExecute.Should().BeFalse();
        result.Value.Warnings.Should().Contain(w => w.Contains("Production"));
    }

    [Fact]
    public async Task PreviewAsync_NoSendPermission_CanExecuteIsFalse()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace(hasSendPermission: false);
        SetupSignatureWithMessages(BuildMessage(1));

        var result = await sut.PreviewAsync(OwnerId, new SignatureReplayPreviewRequest(Filter(_namespaceId)));

        result.Value.CanExecute.Should().BeFalse();
        result.Value.Warnings.Should().Contain(w => w.Contains("Send permission"));
    }

    [Fact]
    public async Task PreviewAsync_NamespaceOwnedByAnotherOwner_ReturnsNotFound()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace(ownerId: "entra:someone-else");

        var result = await sut.PreviewAsync(OwnerId, new SignatureReplayPreviewRequest(Filter(_namespaceId)));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    // ── StartAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task StartAsync_ValidRequest_CreatesPendingJob()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace();
        SetupSignatureWithMessages(BuildMessage(1), BuildMessage(2));

        var result = await sut.StartAsync(OwnerId, new SignatureReplayStartRequest(Filter(_namespaceId)), TestActor);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalMatched.Should().Be(2);
        result.Value.OperationType.Should().Be("Replay");
        result.Value.Status.Should().Be(nameof(BulkOperationStatus.Pending));

        var stored = await _dbContext.SignatureReplayJobs.FirstOrDefaultAsync(j => j.Id == result.Value.Id);
        stored.Should().NotBeNull();
        stored!.Status.Should().Be(BulkOperationStatus.Pending);
        _queueMock.Verify(q => q.Enqueue(result.Value.Id), Times.Once);
    }

    [Fact]
    public async Task StartAsync_ProductionNamespace_Fails()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace(EnvironmentType.Prod);
        SetupSignatureWithMessages(BuildMessage(1));

        var result = await sut.StartAsync(OwnerId, new SignatureReplayStartRequest(Filter(_namespaceId)), TestActor);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SignatureReplay.NotAllowed");
    }

    [Fact]
    public async Task StartAsync_NoMatchingMessages_Fails()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace();
        SetupNoClusteredSignature();

        var result = await sut.StartAsync(OwnerId, new SignatureReplayStartRequest(Filter(_namespaceId)), TestActor);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("SignatureReplay.NoMatches");
    }

    // ── GetJobAsync / CancelJobAsync ─────────────────────────────────────────

    [Fact]
    public async Task GetJobAsync_UnknownJob_ReturnsNotFound()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);

        var result = await sut.GetJobAsync(OwnerId, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    [Fact]
    public async Task CancelJobAsync_PendingJob_MarksNotCancellableAfterRequest()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace();
        SetupSignatureWithMessages(BuildMessage(1));
        var created = await sut.StartAsync(OwnerId, new SignatureReplayStartRequest(Filter(_namespaceId)), TestActor);

        var cancelled = await sut.CancelJobAsync(OwnerId, created.Value.Id);

        cancelled.IsSuccess.Should().BeTrue();
        var stored = await _dbContext.SignatureReplayJobs.FirstOrDefaultAsync(j => j.Id == created.Value.Id);
        stored!.CancellationRequestedAt.Should().NotBeNull();
        _queueMock.Verify(q => q.RequestCancellation(created.Value.Id), Times.Once);
    }

    [Fact]
    public async Task CancelJobAsync_UnknownJob_ReturnsNotFound()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);

        var result = await sut.CancelJobAsync(OwnerId, Guid.NewGuid());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Contain("NotFound");
    }

    // ── ListJobsAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task ListJobsAsync_ReturnsJobsForSignature_MostRecentFirst()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);
        SetupNamespace();
        SetupSignatureWithMessages(BuildMessage(1));
        var first = await sut.StartAsync(OwnerId, new SignatureReplayStartRequest(Filter(_namespaceId)), TestActor);
        SetupSignatureWithMessages(BuildMessage(2));
        var second = await sut.StartAsync(OwnerId, new SignatureReplayStartRequest(Filter(_namespaceId)), TestActor);

        var result = await sut.ListJobsAsync(OwnerId, _namespaceId, SignatureHash, page: 1, pageSize: 20);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(2);
        result.Value.Items.Select(i => i.Id).Should().ContainInOrder(second.Value.Id, first.Value.Id);
    }

    [Fact]
    public async Task ListJobsAsync_NoJobs_ReturnsEmptyPage()
    {
        var sut = CreateSut(BuildProviderMock(CloudProviderType.Aws).Object);

        var result = await sut.ListJobsAsync(OwnerId, _namespaceId, SignatureHash, page: 1, pageSize: 20);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
        result.Value.TotalCount.Should().Be(0);
    }
}

internal static class MockExtensions
{
    public static Mock<T> Also<T>(this Mock<T> mock, Action<Mock<T>> configure) where T : class
    {
        configure(mock);
        return mock;
    }
}
