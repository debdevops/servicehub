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

public sealed class IncidentReadModelServiceTests : IDisposable
{
    private const string OwnerId = "owner-a";
    private const string SignatureHash = "sig-abc123";
    private static readonly Guid NamespaceId = Guid.NewGuid();

    private readonly DlqDbContext _dbContext;
    private readonly Mock<ISignatureLifecycleService> _lifecycleMock = new();
    private readonly Mock<IRecoveryLedger> _recoveryLedgerMock = new();
    private readonly Mock<IPlaybookLedger> _playbookLedgerMock = new();
    private readonly Mock<INamespaceRepository> _namespaceRepositoryMock = new();
    private readonly IncidentReadModelService _service;

    public IncidentReadModelServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new IncidentReadModelService(
            _dbContext, _lifecycleMock.Object, _recoveryLedgerMock.Object, _playbookLedgerMock.Object,
            _namespaceRepositoryMock.Object);

        SetupNoRecoveryEntries();
        SetupNoPlaybookEntries();
        SetupDefaultLifecycle();
        SetupNamespaceLookupFails();
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private void SetupNoRecoveryEntries() => SetupRecoveryEntries();

    private void SetupRecoveryEntries(params RecoveryLedgerEntry[] entries) =>
        _recoveryLedgerMock
            .Setup(r => r.FindEntriesForSignatureSinceAsync(
                OwnerId, SignatureHash, It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RecoveryLedgerEntry>)entries);

    private void SetupNoPlaybookEntries() => SetupPlaybookEntries();

    private void SetupPlaybookEntries(params PlaybookEntry[] entries) =>
        _playbookLedgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, null, NamespaceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(entries));

    private void SetupPlaybookQueryFails() =>
        _playbookLedgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, null, NamespaceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<PlaybookEntry>>(Error.Internal("ERR", "boom")));

    private void SetupDefaultLifecycle() =>
        _lifecycleMock
            .Setup(l => l.GetStatusAsync(OwnerId, NamespaceId, SignatureHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SignatureLifecycleSnapshot(
                SignatureLifecycleStatus.Active, null, null, null)));

    private void SetupNamespaceLookupFails() =>
        _namespaceRepositoryMock
            .Setup(r => r.GetByIdAsync(NamespaceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<Namespace>(Error.NotFound("Namespace.NotFound", "not found")));

    private async Task SeedNamespaceSignatureAsync()
    {
        _dbContext.NamespaceSignatures.Add(new NamespaceSignature
        {
            NamespaceId = NamespaceId,
            OwnerId = OwnerId,
            SignatureHash = SignatureHash,
            FirstSeenAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            LastSeenAt = new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero),
            OccurrenceCount = 4,
            DominantDeadletterReason = "MaxDeliveryCountExceeded",
            TopTermsJson = """["timeout","sql"]""",
        });
        await _dbContext.SaveChangesAsync();
    }

    private static RecoveryLedgerEntry BuildRecoveryEntry(RecoveryEntryState state) => new()
    {
        OperationId = Guid.NewGuid(),
        OwnerId = OwnerId,
        BodyHash = "body-hash",
        TargetEntity = "orders-dlq",
        BegunAt = DateTimeOffset.UtcNow,
        SignatureHashSnapshot = SignatureHash,
        State = state,
    };

    private static PlaybookEntry BuildPlaybookEntry(
        string proposalKind, PlaybookEntryState state, string? signatureHash = SignatureHash) => new()
    {
        OwnerId = OwnerId,
        PillarKind = PillarKind.Investigate,
        ProposalKind = proposalKind,
        EvidenceRefJson = "{}",
        ProposalJson = "{}",
        ProposedAt = DateTimeOffset.UtcNow,
        ProposerIdentity = "System:Test",
        ProposerKind = PlaybookActorKind.System,
        SignatureHashSnapshot = signatureHash,
        NamespaceId = NamespaceId,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        State = state,
    };

    [Fact]
    public void Constructor_NullDbContext_Throws()
    {
        var act = () => new IncidentReadModelService(
            null!, _lifecycleMock.Object, _recoveryLedgerMock.Object, _playbookLedgerMock.Object,
            _namespaceRepositoryMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
    }

    [Fact]
    public void Constructor_NullLifecycleService_Throws()
    {
        var act = () => new IncidentReadModelService(
            _dbContext, null!, _recoveryLedgerMock.Object, _playbookLedgerMock.Object,
            _namespaceRepositoryMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("lifecycle");
    }

    [Fact]
    public async Task GetIncidentAsync_NothingRecordedAnywhere_ReturnsNotFound()
    {
        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetIncidentAsync_EmptySignatureHash_ReturnsNotFound_WithoutQueryingLedgers()
    {
        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, string.Empty);

        result.IsFailure.Should().BeTrue();
        _recoveryLedgerMock.Verify(
            r => r.FindEntriesForSignatureSinceAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(), It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
        _playbookLedgerMock.Verify(
            l => l.QueryEntriesAsync(
                It.IsAny<string>(), It.IsAny<PillarKind?>(), It.IsAny<Guid?>(), It.IsAny<PlaybookEntryState?>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetIncidentAsync_OnlyNamespaceSignatureRecorded_BuildsFromSignatureFields()
    {
        await SeedNamespaceSignatureAsync();

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        var incident = result.Value;
        incident.OccurrenceCount.Should().Be(4);
        incident.DominantDeadletterReason.Should().Be("MaxDeliveryCountExceeded");
        incident.TopTerms.Should().BeEquivalentTo(new[] { "timeout", "sql" });
        incident.RecoveryEntries.Should().BeEmpty();
        incident.PlaybookEntries.Should().BeEmpty();
        incident.Summary.RecoveryEntryCount.Should().Be(0);
    }

    [Fact]
    public async Task GetIncidentAsync_OnlyRecoveryActivity_NoSignatureRow_StillFound()
    {
        // A signature can have recovery/playbook activity recorded against it before (or without)
        // ever being persisted as a NamespaceSignature row (e.g. a manual single-message replay
        // outside a scan) — this must still resolve, not 404.
        SetupRecoveryEntries(BuildRecoveryEntry(RecoveryEntryState.Recovered));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.RecoveryEntries.Should().ContainSingle();
        result.Value.OccurrenceCount.Should().Be(0);
    }

    [Fact]
    public async Task GetIncidentAsync_PlaybookEntries_FiltersToMatchingSignatureOnly()
    {
        SetupPlaybookEntries(
            BuildPlaybookEntry("AnomalyFlag", PlaybookEntryState.Proposed, signatureHash: SignatureHash),
            BuildPlaybookEntry("DriftFinding", PlaybookEntryState.Proposed, signatureHash: "some-other-sig"),
            BuildPlaybookEntry("CorrelationHypothesis", PlaybookEntryState.Proposed, signatureHash: null));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.PlaybookEntries.Should().ContainSingle().Which.ProposalKind.Should().Be("AnomalyFlag");
    }

    [Fact]
    public async Task GetIncidentAsync_SummaryCounts_OpenAndPendingRecoveryStates()
    {
        SetupRecoveryEntries(
            BuildRecoveryEntry(RecoveryEntryState.Executing),
            BuildRecoveryEntry(RecoveryEntryState.Observing),
            BuildRecoveryEntry(RecoveryEntryState.Declined),
            BuildRecoveryEntry(RecoveryEntryState.Recovered));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.Value.Summary.RecoveryEntryCount.Should().Be(4);
        result.Value.Summary.OpenRecoveryEntryCount.Should().Be(2);
        result.Value.Summary.PendingDecisionCount.Should().Be(1);
    }

    [Fact]
    public async Task GetIncidentAsync_SummaryCounts_PendingPlaybookStatesAndProposalKinds()
    {
        SetupPlaybookEntries(
            BuildPlaybookEntry("AnomalyFlag", PlaybookEntryState.Proposed),
            BuildPlaybookEntry("DriftFinding", PlaybookEntryState.UnderReview),
            BuildPlaybookEntry("CorrelationHypothesis", PlaybookEntryState.Approved),
            BuildPlaybookEntry("PreventionTrigger", PlaybookEntryState.Expired),
            BuildPlaybookEntry("ReplayPlan", PlaybookEntryState.Rejected));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        var summary = result.Value.Summary;
        summary.AnomalyFlagCount.Should().Be(1);
        summary.DriftFindingCount.Should().Be(1);
        summary.CorrelationHypothesisCount.Should().Be(1);
        summary.PreventionTriggerCount.Should().Be(1);
        summary.ReplayPlanCount.Should().Be(1);
        // Only the Proposed + UnderReview entries are pending a decision.
        summary.PendingDecisionCount.Should().Be(2);
    }

    [Fact]
    public async Task GetIncidentAsync_PlaybookQueryFails_TreatedAsEmptyRatherThanThrowing()
    {
        SetupPlaybookQueryFails();
        SetupRecoveryEntries(BuildRecoveryEntry(RecoveryEntryState.Recovered));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.IsSuccess.Should().BeTrue();
        result.Value.PlaybookEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetIncidentAsync_NamespaceLookupFails_FallsBackToSnapshotName()
    {
        SetupRecoveryEntries(new RecoveryLedgerEntry
        {
            OperationId = Guid.NewGuid(),
            OwnerId = OwnerId,
            BodyHash = "body-hash",
            TargetEntity = "orders-dlq",
            BegunAt = DateTimeOffset.UtcNow,
            SignatureHashSnapshot = SignatureHash,
            NamespaceNameSnapshot = "prod-orders",
            State = RecoveryEntryState.Recovered,
        });

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.Value.NamespaceName.Should().Be("prod-orders");
    }

    [Fact]
    public async Task GetIncidentAsync_LifecycleStatus_ReflectsServiceResult()
    {
        SetupRecoveryEntries(BuildRecoveryEntry(RecoveryEntryState.Recovered));
        _lifecycleMock
            .Setup(l => l.GetStatusAsync(OwnerId, NamespaceId, SignatureHash, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new SignatureLifecycleSnapshot(
                SignatureLifecycleStatus.Resolved, SignatureLifecycleStatus.Active, DateTimeOffset.UtcNow, null)));

        var result = await _service.GetIncidentAsync(OwnerId, NamespaceId, SignatureHash);

        result.Value.LifecycleStatus.Should().Be("Resolved");
    }
}
