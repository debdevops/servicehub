using FluentAssertions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.PlaybookLedger;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.PlaybookLedger;

public sealed class BacktestServiceTests
{
    private const string OwnerId = "owner-a";

    private readonly Mock<IPlaybookLedger> _playbookLedgerMock = new();
    private readonly Mock<IRecoveryLedger> _recoveryLedgerMock = new();
    private readonly BacktestService _service;

    public BacktestServiceTests()
    {
        _service = new BacktestService(_playbookLedgerMock.Object, _recoveryLedgerMock.Object);
    }

    private static PlaybookEntry BuildEntry(
        PlaybookEntryState state,
        string proposalKind = "AnomalyFlag",
        PillarKind pillarKind = PillarKind.Investigate,
        Guid? namespaceId = null,
        bool fleetWide = false,
        string proposalJson = """{"EntityName":"orders-dlq"}""",
        PlaybookDisposition? disposition = null) => new()
    {
        OwnerId = OwnerId,
        PillarKind = pillarKind,
        ProposalKind = proposalKind,
        EvidenceRefJson = "{}",
        ProposalJson = proposalJson,
        ProposedAt = DateTimeOffset.UtcNow,
        ProposerIdentity = "System:Test",
        ProposerKind = PlaybookActorKind.System,
        NamespaceId = fleetWide ? null : namespaceId ?? Guid.NewGuid(),
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        State = state,
        Disposition = disposition ?? (state == PlaybookEntryState.Approved ? PlaybookDisposition.Approved
            : state == PlaybookEntryState.Rejected ? PlaybookDisposition.Rejected : null),
    };

    private void SetupPlaybookQuery(params PlaybookEntry[] entries) =>
        _playbookLedgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(entries));

    private void SetupRecoveryLookup(Guid namespaceId, string entityName, params RecoveryLedgerEntry[] entries) =>
        _recoveryLedgerMock
            .Setup(r => r.FindEntriesForEntitySinceAsync(
                OwnerId, namespaceId, entityName, It.IsAny<DateTimeOffset>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RecoveryLedgerEntry>)entries);

    private static RecoveryLedgerEntry BuildRecoveryEntry(RecoveryDisposition disposition) => new()
    {
        OperationId = Guid.NewGuid(),
        OwnerId = OwnerId,
        BodyHash = "irrelevant-hash",
        TargetEntity = "orders-dlq",
        BegunAt = DateTimeOffset.UtcNow,
        State = RecoveryEntryState.Recovered,
        Disposition = disposition,
    };

    [Fact]
    public void Constructor_NullPlaybookLedger_Throws()
    {
        var act = () => new BacktestService(null!, _recoveryLedgerMock.Object);
        act.Should().Throw<ArgumentNullException>().WithParameterName("playbookLedger");
    }

    [Fact]
    public void Constructor_NullRecoveryLedger_Throws()
    {
        var act = () => new BacktestService(_playbookLedgerMock.Object, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("recoveryLedger");
    }

    [Fact]
    public async Task GetReportAsync_EmptyOwnerId_Throws()
    {
        var act = () => _service.GetReportAsync(string.Empty);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("ownerId");
    }

    [Fact]
    public async Task GetReportAsync_PlaybookQueryFails_ReturnsEmptyReportRatherThanThrowing()
    {
        _playbookLedgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, null, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<PlaybookEntry>>(Error.Internal("ERR", "boom")));

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(0);
        report.CorroborationRate.Should().BeNull();
    }

    [Fact]
    public async Task GetReportAsync_NoEntries_ReportsZerosAndNullRate()
    {
        SetupPlaybookQuery();

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(0);
        report.CorroboratedCount.Should().Be(0);
        report.CorroborationRate.Should().BeNull();
    }

    [Fact]
    public async Task GetReportAsync_ExcludesNonTerminalStates()
    {
        SetupPlaybookQuery(
            BuildEntry(PlaybookEntryState.Proposed),
            BuildEntry(PlaybookEntryState.UnderReview),
            BuildEntry(PlaybookEntryState.Edited));

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(0);
        _recoveryLedgerMock.Verify(
            r => r.FindEntriesForEntitySinceAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>(),
                It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetReportAsync_ExcludesNonBacktestableProposalKinds()
    {
        SetupPlaybookQuery(BuildEntry(PlaybookEntryState.Approved, proposalKind: "CorrelationHypothesis"));

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(0);
    }

    [Fact]
    public async Task GetReportAsync_ReplayPlanProposal_IsBacktestable()
    {
        // Roadmap item 14's "same engine, second application" on the Recover side:
        // AutoReplayExecutor's ReplayPlan proposals join identically to AnomalyFlag/DriftFinding —
        // same EntityName-in-ProposalJson shape, no separate code path.
        var namespaceId = Guid.NewGuid();
        SetupPlaybookQuery(BuildEntry(
            PlaybookEntryState.Approved, proposalKind: "ReplayPlan", pillarKind: PillarKind.Recover,
            namespaceId: namespaceId));
        SetupRecoveryLookup(namespaceId, "orders-dlq", BuildRecoveryEntry(RecoveryDisposition.Recovered));

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(1);
        report.CorroboratedCount.Should().Be(1);
        report.Entries.Should().ContainSingle().Which.PillarKind.Should().Be(PillarKind.Recover);
    }

    [Fact]
    public async Task GetReportAsync_PreventionTriggerProposal_IsBacktestable_EvenWhileStillProposed()
    {
        // P5's payoff (PREVENTION-RULE-DESIGN-2026-08-29.md §11/§12): a PreventionTrigger is pure
        // evidence, never a decision request — it is never dispositioned, so unlike every other
        // backtestable ProposalKind here it must become a candidate from Proposed (or Expired),
        // never Approved/Rejected, or the join would be permanently unreachable dead code.
        var namespaceId = Guid.NewGuid();
        SetupPlaybookQuery(BuildEntry(
            PlaybookEntryState.Proposed, proposalKind: "PreventionTrigger", pillarKind: PillarKind.Prevent,
            namespaceId: namespaceId));
        SetupRecoveryLookup(namespaceId, "orders-dlq", BuildRecoveryEntry(RecoveryDisposition.Recovered));

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(1);
        report.CorroboratedCount.Should().Be(1);
        report.Entries.Should().ContainSingle().Which.PillarKind.Should().Be(PillarKind.Prevent);
    }

    [Fact]
    public async Task GetReportAsync_PreventionTriggerProposal_ApprovedState_StillBacktestable()
    {
        // Defence-in-depth: even though a trigger is never actually dispositioned in practice
        // (§12), Approved must not be excluded for it either — the candidacy check is additive
        // (Proposed/UnderReview/Expired) for this one ProposalKind, not a replacement gate.
        var namespaceId = Guid.NewGuid();
        SetupPlaybookQuery(BuildEntry(
            PlaybookEntryState.Approved, proposalKind: "PreventionTrigger", pillarKind: PillarKind.Prevent,
            namespaceId: namespaceId));
        SetupRecoveryLookup(namespaceId, "orders-dlq", BuildRecoveryEntry(RecoveryDisposition.Recovered));

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_FleetWideEntryWithNoNamespace_Excluded()
    {
        SetupPlaybookQuery(BuildEntry(PlaybookEntryState.Approved, fleetWide: true));

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(0);
    }

    [Fact]
    public async Task GetReportAsync_UnparsableProposalJson_SkipsEntry()
    {
        SetupPlaybookQuery(BuildEntry(PlaybookEntryState.Approved, proposalJson: "not json"));

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(0);
    }

    [Fact]
    public async Task GetReportAsync_ProposalJsonMissingEntityName_SkipsEntry()
    {
        SetupPlaybookQuery(BuildEntry(PlaybookEntryState.Approved, proposalJson: """{"severity":"high"}"""));

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(0);
    }

    [Fact]
    public async Task GetReportAsync_SubsequentRecoveryActivityExists_Corroborated()
    {
        var namespaceId = Guid.NewGuid();
        SetupPlaybookQuery(BuildEntry(PlaybookEntryState.Approved, namespaceId: namespaceId));
        SetupRecoveryLookup(namespaceId, "orders-dlq", BuildRecoveryEntry(RecoveryDisposition.Recovered));

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(1);
        report.CorroboratedCount.Should().Be(1);
        report.CorroborationRate.Should().Be(1.0);
        report.Entries.Should().ContainSingle().Which.SubsequentRecoveredCount.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_NoSubsequentRecoveryActivity_NotCorroborated()
    {
        var namespaceId = Guid.NewGuid();
        SetupPlaybookQuery(BuildEntry(PlaybookEntryState.Rejected, namespaceId: namespaceId));
        SetupRecoveryLookup(namespaceId, "orders-dlq");

        var report = await _service.GetReportAsync(OwnerId);

        report.TotalBacktested.Should().Be(1);
        report.CorroboratedCount.Should().Be(0);
        report.CorroborationRate.Should().Be(0.0);
        report.Entries.Should().ContainSingle().Which.Disposition.Should().Be("Rejected");
    }

    [Fact]
    public async Task GetReportAsync_MixedRecoveredAndReturned_CountsEachSeparately()
    {
        var namespaceId = Guid.NewGuid();
        SetupPlaybookQuery(BuildEntry(PlaybookEntryState.Approved, namespaceId: namespaceId));
        SetupRecoveryLookup(
            namespaceId, "orders-dlq",
            BuildRecoveryEntry(RecoveryDisposition.Recovered),
            BuildRecoveryEntry(RecoveryDisposition.Returned),
            BuildRecoveryEntry(RecoveryDisposition.Returned));

        var report = await _service.GetReportAsync(OwnerId);

        var result = report.Entries.Should().ContainSingle().Subject;
        result.SubsequentRecoveryAttempts.Should().Be(3);
        result.SubsequentRecoveredCount.Should().Be(1);
        result.SubsequentReturnedCount.Should().Be(2);
    }

    [Fact]
    public async Task GetReportAsync_PassesPillarKindFilterThroughToPlaybookQuery()
    {
        _playbookLedgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Prevent, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(Array.Empty<PlaybookEntry>()));

        await _service.GetReportAsync(OwnerId, PillarKind.Prevent);

        _playbookLedgerMock.Verify(
            l => l.QueryEntriesAsync(OwnerId, PillarKind.Prevent, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
