using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Incidents;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.Incidents;

public sealed class AttentionQueueServiceTests : IDisposable
{
    private const string OwnerId = "owner-a";

    private readonly DlqDbContext _dbContext;
    private readonly Mock<ISignatureLifecycleService> _lifecycleMock = new();
    private readonly Mock<IRecoveryLedger> _recoveryLedgerMock = new();
    private readonly Mock<IPlaybookLedger> _playbookLedgerMock = new();
    private readonly Mock<IFleetOverviewService> _fleetOverviewMock = new();
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly AttentionQueueService _service;

    public AttentionQueueServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new AttentionQueueService(
            _dbContext, _lifecycleMock.Object, _recoveryLedgerMock.Object, _playbookLedgerMock.Object,
            _fleetOverviewMock.Object, _namespaceRepositoryMock.Object);

        SetupNoDeclinedEntries();
        SetupNoPendingPlaybookEntries();
        SetupNoFleetOverview();

        // Default: every namespace referenced by a seeded signature is still registered.
        _namespaceRepositoryMock
            .Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<IReadOnlySet<Guid>?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result<IReadOnlyList<Namespace>>.Success(
                _dbContext.NamespaceSignatures
                    .Select(s => s.NamespaceId)
                    .Distinct()
                    .Select(MakeNamespace)
                    .ToList()));
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static Namespace MakeNamespace(Guid id)
    {
        var ns = Namespace.Create("test-ns", "PROTECTED:encrypted-data").Value;
        typeof(Namespace).GetProperty("Id")!.SetValue(ns, id);
        return ns;
    }

    private void SetupNoDeclinedEntries() =>
        _recoveryLedgerMock
            .Setup(r => r.QueryEntriesAsync(It.IsAny<RecoveryEntryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RecoveryLedgerEntry>)Array.Empty<RecoveryLedgerEntry>());

    private void SetupDeclinedEntries(params RecoveryLedgerEntry[] entries) =>
        _recoveryLedgerMock
            .Setup(r => r.QueryEntriesAsync(It.IsAny<RecoveryEntryQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RecoveryLedgerEntry>)entries);

    private void SetupNoPendingPlaybookEntries() => SetupPlaybookEntries();

    private void SetupPlaybookEntries(params PlaybookEntry[] entries) =>
        _playbookLedgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(entries));

    private void SetupNoFleetOverview() =>
        _fleetOverviewMock
            .Setup(f => f.GetOverviewAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FleetOverview>.Success(
                new FleetOverview(DateTimeOffset.UtcNow, 24, 0, 0, 0, 0, [], new Dictionary<string, int>(), [])));

    private void SetupSeverity(params FleetNamespaceHealth[] namespaces) =>
        _fleetOverviewMock
            .Setup(f => f.GetOverviewAsync(OwnerId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<FleetOverview>.Success(
                new FleetOverview(DateTimeOffset.UtcNow, 24, namespaces.Length, 0, 0, 0, namespaces, new Dictionary<string, int>(), [])));

    private static FleetNamespaceHealth MakeHealth(Guid namespaceId, FleetHealthSeverity severity) => new(
        NamespaceId: namespaceId,
        NamespaceName: "test-ns",
        Provider: "Azure",
        Environment: "Prod",
        ActiveCount: 1,
        NewInWindow: 0,
        ResolvedInWindow: 0,
        TotalCount: 1,
        TopEntity: null,
        TopEntityCount: 0,
        TopCategory: null,
        OldestActiveDetectedAt: null,
        Severity: severity,
        Coverage: FleetMonitoringCoverage.Scanned,
        CoverageNote: null);

    private void SetupLifecycle(Guid namespaceId, string signatureHash, SignatureLifecycleStatus status, SignatureLifecycleStatus? previous = null) =>
        _lifecycleMock
            .Setup(l => l.GetStatusAsync(OwnerId, namespaceId, signatureHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SignatureLifecycleSnapshot(status, previous, null, null)));

    private async Task<NamespaceSignature> SeedSignatureAsync(
        Guid namespaceId, string hash, int occurrenceCount = 1, DateTimeOffset? lastSeenAt = null)
    {
        var sig = new NamespaceSignature
        {
            NamespaceId = namespaceId,
            OwnerId = OwnerId,
            SignatureHash = hash,
            FirstSeenAt = DateTimeOffset.UtcNow.AddDays(-10),
            LastSeenAt = lastSeenAt ?? DateTimeOffset.UtcNow,
            OccurrenceCount = occurrenceCount,
            DominantDeadletterReason = "MaxDeliveryCountExceeded",
            TopTermsJson = "[\"timeout\"]",
        };
        _dbContext.NamespaceSignatures.Add(sig);
        await _dbContext.SaveChangesAsync();
        SetupLifecycle(namespaceId, hash, SignatureLifecycleStatus.Active);
        return sig;
    }

    private static RecoveryLedgerEntry BuildDeclinedEntry(string signatureHash) => new()
    {
        OperationId = Guid.NewGuid(),
        OwnerId = OwnerId,
        BodyHash = "body-hash",
        TargetEntity = "orders-dlq",
        BegunAt = DateTimeOffset.UtcNow,
        SignatureHashSnapshot = signatureHash,
        State = RecoveryEntryState.Declined,
    };

    private static PlaybookEntry BuildPendingPlaybookEntry(string signatureHash, Guid namespaceId) => new()
    {
        OwnerId = OwnerId,
        PillarKind = PillarKind.Investigate,
        ProposalKind = "AnomalyFlag",
        EvidenceRefJson = "{}",
        ProposalJson = "{}",
        ProposedAt = DateTimeOffset.UtcNow,
        ProposerIdentity = "System:Test",
        ProposerKind = PlaybookActorKind.System,
        SignatureHashSnapshot = signatureHash,
        NamespaceId = namespaceId,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        State = PlaybookEntryState.Proposed,
    };

    [Fact]
    public void Constructor_NullDbContext_Throws()
    {
        var act = () => new AttentionQueueService(
            null!, _lifecycleMock.Object, _recoveryLedgerMock.Object, _playbookLedgerMock.Object,
            _fleetOverviewMock.Object, _namespaceRepositoryMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public async Task GetAttentionQueueAsync_NoSignatures_ReturnsEmptyQueue()
    {
        var result = await _service.GetAttentionQueueAsync(OwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.IsEmpty.Should().BeTrue();
        result.Value.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAttentionQueueAsync_ResolvedSignatureWithNoPendingDecision_IsExcluded()
    {
        var namespaceId = Guid.NewGuid();
        var sig = await SeedSignatureAsync(namespaceId, "sig-resolved");
        SetupLifecycle(namespaceId, sig.SignatureHash, SignatureLifecycleStatus.Resolved);

        var result = await _service.GetAttentionQueueAsync(OwnerId);

        result.Value.Items.Should().BeEmpty();
        result.Value.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task GetAttentionQueueAsync_ResolvedSignatureWithPendingDecision_NeverHidesBehindLifecycleFilter()
    {
        var namespaceId = Guid.NewGuid();
        var sig = await SeedSignatureAsync(namespaceId, "sig-resolved-pending");
        SetupLifecycle(namespaceId, sig.SignatureHash, SignatureLifecycleStatus.Resolved);
        SetupDeclinedEntries(BuildDeclinedEntry(sig.SignatureHash));

        var result = await _service.GetAttentionQueueAsync(OwnerId);

        result.Value.Items.Should().ContainSingle(i => i.SignatureHash == sig.SignatureHash);
        result.Value.Items[0].PendingDecisionCount.Should().Be(1);
    }

    [Fact]
    public async Task GetAttentionQueueAsync_MoreThanThreeCandidates_CapsAtThree()
    {
        var namespaceId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await SeedSignatureAsync(namespaceId, $"sig-hash-{i}");
        }

        var result = await _service.GetAttentionQueueAsync(OwnerId);

        result.Value.Items.Should().HaveCount(3);
        result.Value.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public async Task GetAttentionQueueAsync_PendingDecisionAlwaysOutranksNonBlockingCandidates()
    {
        var namespaceId = Guid.NewGuid();
        // Four non-blocking, high-volume candidates plus one low-volume but decision-blocked one —
        // without the decision-blocking weight, the low-volume item would be capped out.
        for (var i = 0; i < 4; i++)
        {
            await SeedSignatureAsync(namespaceId, $"sig-volume-{i}", occurrenceCount: 50);
        }
        var blocked = await SeedSignatureAsync(namespaceId, "sig-blocked", occurrenceCount: 1);
        SetupDeclinedEntries(BuildDeclinedEntry(blocked.SignatureHash));

        var result = await _service.GetAttentionQueueAsync(OwnerId);

        result.Value.Items.Should().Contain(i => i.SignatureHash == blocked.SignatureHash);
        result.Value.Items[0].SignatureHash.Should().Be(blocked.SignatureHash);
    }

    [Fact]
    public async Task GetAttentionQueueAsync_PendingPlaybookEntry_CountsTowardPendingDecisionCount()
    {
        var namespaceId = Guid.NewGuid();
        var sig = await SeedSignatureAsync(namespaceId, "sig-playbook-pending");
        SetupPlaybookEntries(BuildPendingPlaybookEntry(sig.SignatureHash, namespaceId));

        var result = await _service.GetAttentionQueueAsync(OwnerId);

        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].PendingDecisionCount.Should().Be(1);
        result.Value.Items[0].RecommendedAction.Should().Be("Review pending decision");
    }

    [Fact]
    public async Task GetAttentionQueueAsync_EscalatingSignature_MarkedRecurringWithEscalationRecommendation()
    {
        var namespaceId = Guid.NewGuid();
        var sig = await SeedSignatureAsync(namespaceId, "sig-escalating");
        SetupLifecycle(namespaceId, sig.SignatureHash, SignatureLifecycleStatus.Reopened, SignatureLifecycleStatus.Resolved);

        var result = await _service.GetAttentionQueueAsync(OwnerId);

        result.Value.Items.Should().ContainSingle();
        result.Value.Items[0].IsRecurring.Should().BeTrue();
        result.Value.Items[0].RecommendedAction.Should().Be("Review escalation");
    }

    [Fact]
    public async Task GetAttentionQueueAsync_CriticalNamespaceSeverity_RanksAboveHealthyNamespace()
    {
        var criticalNamespaceId = Guid.NewGuid();
        var healthyNamespaceId = Guid.NewGuid();
        var critical = await SeedSignatureAsync(criticalNamespaceId, "sig-critical");
        var healthy = await SeedSignatureAsync(healthyNamespaceId, "sig-healthy");
        SetupSeverity(
            MakeHealth(criticalNamespaceId, FleetHealthSeverity.Critical),
            MakeHealth(healthyNamespaceId, FleetHealthSeverity.Healthy));

        var result = await _service.GetAttentionQueueAsync(OwnerId);

        var criticalItem = result.Value.Items.Single(i => i.SignatureHash == critical.SignatureHash);
        var healthyItem = result.Value.Items.Single(i => i.SignatureHash == healthy.SignatureHash);
        criticalItem.Severity.Should().Be(nameof(FleetHealthSeverity.Critical));
        criticalItem.Score.Should().BeGreaterThan(healthyItem.Score);
    }

    [Fact]
    public async Task GetAttentionQueueAsync_NamespaceNoLongerRegistered_ExcludesOrphanedSignature()
    {
        var deletedNamespaceId = Guid.NewGuid();
        var liveNamespaceId = Guid.NewGuid();
        await SeedSignatureAsync(deletedNamespaceId, "sig-orphaned");
        var live = await SeedSignatureAsync(liveNamespaceId, "sig-live");

        _namespaceRepositoryMock
            .Setup(r => r.GetByOwnerAsync(OwnerId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success(
                new List<Namespace> { MakeNamespace(liveNamespaceId) }));

        var result = await _service.GetAttentionQueueAsync(OwnerId);

        result.Value.Items.Should().ContainSingle(i => i.SignatureHash == live.SignatureHash);
    }
}
