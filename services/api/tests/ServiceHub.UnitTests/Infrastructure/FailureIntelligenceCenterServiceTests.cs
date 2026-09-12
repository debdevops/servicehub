using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure;

public sealed class FailureIntelligenceCenterServiceTests : IDisposable
{
    private const string OwnerId = "entra:test-owner-123";

    private readonly DlqDbContext _dbContext;
    private readonly Mock<INamespaceSignatureLookupService> _signatureLookupMock = new();
    private readonly Mock<ISignatureLifecycleService> _lifecycleMock = new();
    private readonly Mock<IFailureKnowledgeService> _knowledgeMock = new();
    private readonly Mock<IFleetOverviewService> _fleetOverviewMock = new();
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly FailureIntelligenceCenterService _sut;

    public FailureIntelligenceCenterServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _lifecycleMock
            .Setup(l => l.GetStatusAsync(It.IsAny<string>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SignatureLifecycleSnapshot>.Success(
                new SignatureLifecycleSnapshot(SignatureLifecycleStatus.Active, null, null, null)));

        _fleetOverviewMock
            .Setup(f => f.GetOverviewAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FleetOverview>.Success(
                new FleetOverview(DateTimeOffset.UtcNow, 24, 0, 0, 0, 0, [], new Dictionary<string, int>(), [])));

        // Default: every namespace referenced by a seeded signature is still registered, so
        // existing tests (which never exercise deletion) are unaffected by the namespace-registry
        // filter. Tests that specifically cover orphaned signatures override this per-call.
        _namespaceRepositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result<IReadOnlyList<Namespace>>.Success(
                _dbContext.NamespaceSignatures
                    .Select(s => s.NamespaceId)
                    .Distinct()
                    .Select(MakeNamespace)
                    .ToList()));

        _sut = new FailureIntelligenceCenterService(
            _dbContext, _signatureLookupMock.Object, _lifecycleMock.Object, _knowledgeMock.Object,
            _fleetOverviewMock.Object, _namespaceRepositoryMock.Object);
    }

    private static Namespace MakeNamespace(Guid id)
    {
        var ns = Namespace.Create("test-ns", "PROTECTED:encrypted-data").Value;
        typeof(Namespace).GetProperty("Id")!.SetValue(ns, id);
        return ns;
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static NamespaceSignature MakeSignature(
        Guid namespaceId, string hash, SignatureHashKind hashKind = SignatureHashKind.Fingerprint) => new()
    {
        NamespaceId = namespaceId,
        OwnerId = OwnerId,
        SignatureHash = hash,
        HashKind = hashKind,
        FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-10),
        LastSeenAt = DateTimeOffset.UtcNow.AddDays(-1),
        OccurrenceCount = 4,
        DominantDeadletterReason = "MaxDeliveryCountExceeded",
        TopTermsJson = "[\"timeout\"]",
    };

    private static SignatureReplayJob MakeJob(
        Guid namespaceId, string hash, BulkOperationStatus status, DateTimeOffset createdAt,
        int totalMatched = 5, int failureCount = 0) => new()
    {
        OwnerId = OwnerId,
        NamespaceId = namespaceId,
        NamespaceDisplayName = "test-namespace",
        SignatureHash = hash,
        MessageIdsJson = "[]",
        Status = status,
        CreatedAt = createdAt,
        CompletedAt = createdAt.AddMinutes(1),
        TotalMatched = totalMatched,
        ProcessedCount = totalMatched,
        FailureCount = failureCount,
        SuccessCount = totalMatched - failureCount,
        RequestedByIdentity = OwnerId,
        RequestedByActorKind = RecoveryActorKind.User,
    };

    [Fact]
    public async Task GetInvestigationCenterAsync_SignatureNamespaceNoLongerRegistered_ExcludesOrphanedSignature()
    {
        // Deleting a namespace does not cascade-delete its NamespaceSignature rows. Without
        // filtering against the live namespace registry, a deleted namespace's stale signature
        // would keep surfacing in the investigation queue with a dead-end "Investigate" link.
        var deletedNamespaceId = Guid.NewGuid();
        var liveNamespaceId = Guid.NewGuid();
        _dbContext.NamespaceSignatures.Add(MakeSignature(deletedNamespaceId, "hash-orphaned"));
        _dbContext.NamespaceSignatures.Add(MakeSignature(liveNamespaceId, "hash-live"));
        await _dbContext.SaveChangesAsync();

        // Only liveNamespaceId is still registered.
        _namespaceRepositoryMock
            .Setup(r => r.GetByOwnerAsync(OwnerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(
                new List<Namespace> { MakeNamespace(liveNamespaceId) }));

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.InvestigationQueue.Should().NotContain(i => i.NamespaceId == deletedNamespaceId);
        result.Value.InvestigationQueue.Should().Contain(i => i.NamespaceId == liveNamespaceId);
    }

    // Full E2E pass, 2026-09-12: reproduced live with a real flood-seeded signature — Incident
    // Center's "Investigate" link opened IncidentReadModelService.GetIncidentAsync, which (per its
    // own ADR-0009 comment, mirrored by AttentionQueueService) only ever resolves a Fingerprint-
    // kind row. This query had no HashKind filter at all, so a Cluster-kind row for the very same
    // real failure (written by the DLQ Intelligence clustering path) surfaced here as its own
    // "incident" whose "Investigate" link 404'd — confirmed live via
    // GET /api/v1/namespaces/{id}/incidents/{hash} against a real Cluster-kind row.
    [Fact]
    public async Task GetInvestigationCenterAsync_ClusterKindSignature_ExcludedFromInvestigationQueue()
    {
        var namespaceId = Guid.NewGuid();
        var fingerprintSignature = MakeSignature(namespaceId, "hash-fingerprint");
        var clusterSignature = MakeSignature(namespaceId, "hash-cluster", SignatureHashKind.Cluster);
        _dbContext.NamespaceSignatures.Add(fingerprintSignature);
        _dbContext.NamespaceSignatures.Add(clusterSignature);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.InvestigationQueue.Should().Contain(i => i.SignatureHash == "hash-fingerprint");
        result.Value.InvestigationQueue.Should().NotContain(i => i.SignatureHash == "hash-cluster");
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_NoReplayJobs_ReturnsEmptyFailedReplays()
    {
        var namespaceId = Guid.NewGuid();
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, "hash-no-jobs"));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.FailedReplays.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_MostRecentJobFailed_IncludesFailedReplayWithRecommendation()
    {
        var namespaceId = Guid.NewGuid();
        const string hash = "hash-failed";
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, hash));
        _dbContext.SignatureReplayJobs.Add(
            MakeJob(namespaceId, hash, BulkOperationStatus.Failed, DateTimeOffset.UtcNow.AddDays(-1), failureCount: 5));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.FailedReplays.Should().ContainSingle();
        var item = result.Value.FailedReplays[0];
        item.NamespaceId.Should().Be(namespaceId);
        item.SignatureHash.Should().Be(hash);
        item.JobStatus.Should().Be(nameof(BulkOperationStatus.Failed));
        item.RecommendedNextAction.Should().Be("Investigate the underlying failure before replaying again.");
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_MostRecentJobCompletedWithErrors_UsesReviewRecommendation()
    {
        var namespaceId = Guid.NewGuid();
        const string hash = "hash-errors";
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, hash));
        _dbContext.SignatureReplayJobs.Add(
            MakeJob(namespaceId, hash, BulkOperationStatus.CompletedWithErrors, DateTimeOffset.UtcNow.AddHours(-3), failureCount: 2));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.Value.FailedReplays.Should().ContainSingle();
        result.Value.FailedReplays[0].RecommendedNextAction.Should().Be("Review the failure sample before retrying.");
        result.Value.FailedReplays[0].FailedCount.Should().Be(2);
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_LatestJobSucceededAfterEarlierFailure_ExcludesSignature()
    {
        var namespaceId = Guid.NewGuid();
        const string hash = "hash-recovered";
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, hash));
        _dbContext.SignatureReplayJobs.AddRange(
            MakeJob(namespaceId, hash, BulkOperationStatus.Failed, DateTimeOffset.UtcNow.AddDays(-2)),
            MakeJob(namespaceId, hash, BulkOperationStatus.Completed, DateTimeOffset.UtcNow.AddDays(-1)));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.Value.FailedReplays.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_FailedJobOutsideSevenDayWindow_IsExcluded()
    {
        var namespaceId = Guid.NewGuid();
        const string hash = "hash-stale";
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, hash));
        _dbContext.SignatureReplayJobs.Add(
            MakeJob(namespaceId, hash, BulkOperationStatus.Failed, DateTimeOffset.UtcNow.AddDays(-10)));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.Value.FailedReplays.Should().BeEmpty();
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_MultipleFailedSignatures_OrdersByMostRecentFirst()
    {
        var namespaceId = Guid.NewGuid();
        const string olderHash = "hash-older";
        const string newerHash = "hash-newer";
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, olderHash));
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, newerHash));
        _dbContext.SignatureReplayJobs.AddRange(
            MakeJob(namespaceId, olderHash, BulkOperationStatus.Failed, DateTimeOffset.UtcNow.AddDays(-3)),
            MakeJob(namespaceId, newerHash, BulkOperationStatus.Failed, DateTimeOffset.UtcNow.AddHours(-1)));
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.Value.FailedReplays.Should().HaveCount(2);
        result.Value.FailedReplays[0].SignatureHash.Should().Be(newerHash);
        result.Value.FailedReplays[1].SignatureHash.Should().Be(olderHash);
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_FleetOverviewHasUnhealthyNamespaces_PopulatesFleetHealthWithWarningAndCriticalOnly()
    {
        var healthy = MakeNamespaceHealth("healthy-ns", FleetHealthSeverity.Healthy);
        var warning = MakeNamespaceHealth("warning-ns", FleetHealthSeverity.Warning);
        var critical = MakeNamespaceHealth("critical-ns", FleetHealthSeverity.Critical);
        _fleetOverviewMock
            .Setup(f => f.GetOverviewAsync(OwnerId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FleetOverview>.Success(
                new FleetOverview(DateTimeOffset.UtcNow, 24, 3, 7, 2, 1,
                    [critical, warning, healthy], new Dictionary<string, int>(), [])));

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.Value.FleetHealth.Should().NotBeNull();
        result.Value.FleetHealth!.NamespaceCount.Should().Be(3);
        result.Value.FleetHealth.TotalActive.Should().Be(7);
        result.Value.FleetHealth.TopUnhealthyNamespaces.Should().HaveCount(2);
        result.Value.FleetHealth.TopUnhealthyNamespaces.Should().NotContain(n => n.Severity == FleetHealthSeverity.Healthy);
    }

    [Fact]
    public async Task GetInvestigationCenterAsync_FleetOverviewQueryFails_FleetHealthIsNullAndRestOfResponseUnaffected()
    {
        _fleetOverviewMock
            .Setup(f => f.GetOverviewAsync(OwnerId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FleetOverview>.Failure(Error.Internal("Fleet.OverviewFailed", "boom")));

        var result = await _sut.GetInvestigationCenterAsync(OwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.FleetHealth.Should().BeNull();
    }

    [Fact]
    public async Task GetIncidentsListAsync_IncludesSignaturesOfEveryLifecycleStatus()
    {
        var namespaceId = Guid.NewGuid();
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, "hash-active"));
        _dbContext.NamespaceSignatures.Add(MakeSignature(namespaceId, "hash-resolved"));
        _dbContext.SignatureLifecycleStates.Add(new SignatureLifecycleState
        {
            OwnerId = OwnerId,
            NamespaceId = namespaceId,
            SignatureHash = "hash-resolved",
            Status = SignatureLifecycleStatus.Resolved,
            PreviousStatus = SignatureLifecycleStatus.Active,
            TransitionedAt = DateTimeOffset.UtcNow.AddHours(-1),
            CreatedAt = DateTimeOffset.UtcNow.AddHours(-1),
        });
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetIncidentsListAsync(OwnerId, trendDays: 7);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().HaveCount(2);
        result.Value.Items.Should().Contain(i => i.SignatureHash == "hash-active" && i.Status == "Active");
        result.Value.Items.Should().Contain(i => i.SignatureHash == "hash-resolved" && i.Status == "Resolved");
    }

    [Fact]
    public async Task GetIncidentsListAsync_SignatureNamespaceNoLongerRegistered_ExcludesOrphanedSignature()
    {
        var deletedNamespaceId = Guid.NewGuid();
        var liveNamespaceId = Guid.NewGuid();
        _dbContext.NamespaceSignatures.Add(MakeSignature(deletedNamespaceId, "hash-orphaned"));
        _dbContext.NamespaceSignatures.Add(MakeSignature(liveNamespaceId, "hash-live"));
        await _dbContext.SaveChangesAsync();

        _namespaceRepositoryMock
            .Setup(r => r.GetByOwnerAsync(OwnerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(
                new List<Namespace> { MakeNamespace(liveNamespaceId) }));

        var result = await _sut.GetIncidentsListAsync(OwnerId, trendDays: 7);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().NotContain(i => i.NamespaceId == deletedNamespaceId);
        result.Value.Items.Should().Contain(i => i.NamespaceId == liveNamespaceId);
    }

    [Fact]
    public async Task GetIncidentsListAsync_ClusterKindSignature_ExcludedFromList()
    {
        var namespaceId = Guid.NewGuid();
        var fingerprintSignature = MakeSignature(namespaceId, "hash-fingerprint");
        var clusterSignature = MakeSignature(namespaceId, "hash-cluster", SignatureHashKind.Cluster);
        _dbContext.NamespaceSignatures.Add(fingerprintSignature);
        _dbContext.NamespaceSignatures.Add(clusterSignature);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetIncidentsListAsync(OwnerId, trendDays: 7);

        result.IsSuccess.Should().BeTrue();
        result.Value.Items.Should().Contain(i => i.SignatureHash == "hash-fingerprint");
        result.Value.Items.Should().NotContain(i => i.SignatureHash == "hash-cluster");
    }

    [Fact]
    public async Task GetIncidentsListAsync_ComputesTopCategoriesByOccurrenceCount()
    {
        var namespaceId = Guid.NewGuid();
        var high = MakeSignature(namespaceId, "hash-high");
        high.OccurrenceCount = 20;
        _dbContext.NamespaceSignatures.Add(high);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetIncidentsListAsync(OwnerId, trendDays: 7);

        result.Value.TopCategories.Should().ContainSingle();
        result.Value.TopCategories[0].Category.Should().Be("MaxDeliveryCountExceeded");
        result.Value.TopCategories[0].Count.Should().Be(20);
        result.Value.TopCategories[0].Percent.Should().Be(100);
    }

    [Fact]
    public async Task GetIncidentsListAsync_TrendCountsNewSignatureInTodaysBucket()
    {
        var namespaceId = Guid.NewGuid();
        var sig = new NamespaceSignature
        {
            NamespaceId = namespaceId,
            OwnerId = OwnerId,
            SignatureHash = "hash-new",
            HashKind = SignatureHashKind.Fingerprint,
            FirstSeenAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            LastSeenAt = DateTimeOffset.UtcNow.AddMinutes(-30),
            OccurrenceCount = 1,
            DominantDeadletterReason = "MaxDeliveryCountExceeded",
            TopTermsJson = "[\"timeout\"]",
        };
        _dbContext.NamespaceSignatures.Add(sig);
        await _dbContext.SaveChangesAsync();

        var result = await _sut.GetIncidentsListAsync(OwnerId, trendDays: 7);

        result.Value.Trend.Should().NotBeEmpty();
        result.Value.Trend.Sum(t => t.New).Should().Be(1);
        result.Value.Trend.Sum(t => t.Active).Should().Be(1);
    }

    private static FleetNamespaceHealth MakeNamespaceHealth(string name, FleetHealthSeverity severity) => new(
        NamespaceId: Guid.NewGuid(),
        NamespaceName: name,
        Provider: "Azure",
        Environment: "Prod",
        ActiveCount: 3,
        NewInWindow: 1,
        ResolvedInWindow: 0,
        TotalCount: 3,
        TopEntity: "queue-a",
        TopEntityCount: 3,
        TopCategory: "Timeout",
        OldestActiveDetectedAt: DateTimeOffset.UtcNow.AddDays(-1),
        Severity: severity,
        Coverage: FleetMonitoringCoverage.Scanned,
        CoverageNote: null);
}
