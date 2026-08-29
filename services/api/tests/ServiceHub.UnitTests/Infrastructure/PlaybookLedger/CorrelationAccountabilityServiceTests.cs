using FluentAssertions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.PlaybookLedger;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.PlaybookLedger;

public sealed class CorrelationAccountabilityServiceTests
{
    private const string OwnerId = "owner-a";

    private readonly Mock<IPlaybookLedger> _ledgerMock = new();
    private readonly CorrelationAccountabilityService _service;

    public CorrelationAccountabilityServiceTests()
    {
        _service = new CorrelationAccountabilityService(_ledgerMock.Object);
    }

    private static PlaybookEntry BuildEntry(
        PlaybookEntryState state, string proposalKind = "CorrelationHypothesis", PillarKind pillarKind = PillarKind.Correlate) => new()
    {
        OwnerId = OwnerId,
        PillarKind = pillarKind,
        ProposalKind = proposalKind,
        EvidenceRefJson = "{}",
        ProposalJson = "{}",
        ProposedAt = DateTimeOffset.UtcNow,
        ProposerIdentity = "System:CorrelationDetectionWorker",
        ProposerKind = PlaybookActorKind.System,
        ExpiresAt = DateTimeOffset.UtcNow.AddDays(7),
        State = state,
    };

    [Fact]
    public void Constructor_NullPlaybookLedger_Throws()
    {
        var act = () => new CorrelationAccountabilityService(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("playbookLedger");
    }

    [Fact]
    public async Task GetReportAsync_EmptyOwnerId_Throws()
    {
        var act = () => _service.GetReportAsync(string.Empty, CancellationToken.None);
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("ownerId");
    }

    [Fact]
    public async Task GetReportAsync_QueriesCorrelatePillarForTheGivenOwner()
    {
        _ledgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Correlate, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(Array.Empty<PlaybookEntry>()));

        await _service.GetReportAsync(OwnerId, CancellationToken.None);

        _ledgerMock.Verify(
            l => l.QueryEntriesAsync(OwnerId, PillarKind.Correlate, null, null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetReportAsync_NoHypotheses_ReportsZerosAndNullApprovalRate()
    {
        _ledgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Correlate, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(Array.Empty<PlaybookEntry>()));

        var report = await _service.GetReportAsync(OwnerId, CancellationToken.None);

        report.TotalHypotheses.Should().Be(0);
        report.ApprovalRate.Should().BeNull();
    }

    [Fact]
    public async Task GetReportAsync_QueryFails_ReturnsEmptyReportRatherThanThrowing()
    {
        _ledgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Correlate, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Failure<IReadOnlyList<PlaybookEntry>>(Error.Internal("ERR", "boom")));

        var report = await _service.GetReportAsync(OwnerId, CancellationToken.None);

        report.TotalHypotheses.Should().Be(0);
        report.ApprovalRate.Should().BeNull();
    }

    [Fact]
    public async Task GetReportAsync_ExcludesNonCorrelationHypothesisProposalKinds()
    {
        _ledgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Correlate, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(new[]
            {
                BuildEntry(PlaybookEntryState.Approved),
                BuildEntry(PlaybookEntryState.Approved, proposalKind: "SomeOtherProposalKind"),
            }));

        var report = await _service.GetReportAsync(OwnerId, CancellationToken.None);

        report.TotalHypotheses.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_OnlyProposedAndUnderReview_ApprovalRateStaysNull()
    {
        _ledgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Correlate, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(new[]
            {
                BuildEntry(PlaybookEntryState.Proposed),
                BuildEntry(PlaybookEntryState.UnderReview),
            }));

        var report = await _service.GetReportAsync(OwnerId, CancellationToken.None);

        report.TotalHypotheses.Should().Be(2);
        report.ProposedCount.Should().Be(1);
        report.UnderReviewCount.Should().Be(1);
        report.ApprovalRate.Should().BeNull();
    }

    [Fact]
    public async Task GetReportAsync_EditedCountsAsUnderReview()
    {
        _ledgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Correlate, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(new[]
            {
                BuildEntry(PlaybookEntryState.Edited),
            }));

        var report = await _service.GetReportAsync(OwnerId, CancellationToken.None);

        report.UnderReviewCount.Should().Be(1);
    }

    [Fact]
    public async Task GetReportAsync_AllApproved_ApprovalRateIsOne()
    {
        _ledgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Correlate, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(new[]
            {
                BuildEntry(PlaybookEntryState.Approved),
                BuildEntry(PlaybookEntryState.Approved),
            }));

        var report = await _service.GetReportAsync(OwnerId, CancellationToken.None);

        report.ApprovalRate.Should().Be(1.0);
    }

    [Fact]
    public async Task GetReportAsync_AllRejected_ApprovalRateIsZero()
    {
        _ledgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Correlate, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(new[]
            {
                BuildEntry(PlaybookEntryState.Rejected),
            }));

        var report = await _service.GetReportAsync(OwnerId, CancellationToken.None);

        report.ApprovalRate.Should().Be(0.0);
    }

    [Fact]
    public async Task GetReportAsync_ExpiredAndSuperseded_CountedSeparatelyFromApprovalRate()
    {
        _ledgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Correlate, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(new[]
            {
                BuildEntry(PlaybookEntryState.Expired),
                BuildEntry(PlaybookEntryState.Superseded),
                BuildEntry(PlaybookEntryState.Approved),
            }));

        var report = await _service.GetReportAsync(OwnerId, CancellationToken.None);

        report.ExpiredCount.Should().Be(1);
        report.SupersededCount.Should().Be(1);
        report.ApprovalRate.Should().Be(1.0);
    }
}
