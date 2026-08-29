using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Api.Controllers.V1;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.PlaybookLedger;

namespace ServiceHub.UnitTests.Api.Controllers.V1;

public sealed class PlaybookControllerTests : IDisposable
{
    private const string OwnerA = "entra:owner-a";
    private const string OwnerB = "entra:owner-b";

    private readonly DlqDbContext _dbContext;
    private readonly IPlaybookLedger _playbookLedger;
    private readonly ICorrelationAccountabilityService _correlationAccountability;
    private readonly PlaybookController _controller;

    public PlaybookControllerTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _playbookLedger = new PlaybookLedgerService(_dbContext);
        _correlationAccountability = new CorrelationAccountabilityService(_playbookLedger);
        _controller = CreateController(OwnerA);
    }

    private PlaybookController CreateController(string ownerId) => new(_playbookLedger, _correlationAccountability)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                Items = { { "OwnerId", ownerId } }
            }
        }
    };

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private async Task<Guid> ProposeEntryAsync(
        string ownerId,
        PillarKind pillarKind = PillarKind.Investigate,
        string proposalKind = "AnomalyFlag",
        Guid? namespaceId = null)
    {
        var result = await _playbookLedger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = ownerId,
            PillarKind = pillarKind,
            ProposalKind = proposalKind,
            EvidenceRefJson = """{"anomalyId":"abc-123"}""",
            ProposalJson = """{"severity":"high"}""",
            Proposer = new PlaybookActor("System:Test", PlaybookActorKind.System),
            NamespaceId = namespaceId,
            ExpiresAfter = TimeSpan.FromDays(7),
        });
        return result.Value.Id;
    }

    [Fact]
    public void Constructor_NullPlaybookLedger_Throws()
    {
        var act = () => new PlaybookController(null!, _correlationAccountability);
        act.Should().Throw<ArgumentNullException>().WithParameterName("playbookLedger");
    }

    [Fact]
    public void Constructor_NullCorrelationAccountability_Throws()
    {
        var act = () => new PlaybookController(_playbookLedger, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("correlationAccountability");
    }

    // ── GetEntries ──────────────────────────────────────────────────

    [Fact]
    public async Task GetEntries_ReturnsOnlyCallerOwnedEntries()
    {
        await ProposeEntryAsync(OwnerA);
        await ProposeEntryAsync(OwnerB);

        var result = await _controller.GetEntries();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = ok.Value.Should().BeAssignableTo<IReadOnlyList<PlaybookEntryResponse>>().Subject;
        entries.Should().ContainSingle();
    }

    [Fact]
    public async Task GetEntries_PillarKindFilter_OnlyReturnsMatchingPillar()
    {
        await ProposeEntryAsync(OwnerA, pillarKind: PillarKind.Investigate);
        await ProposeEntryAsync(OwnerA, pillarKind: PillarKind.Prevent, proposalKind: "DriftFinding");

        var result = await _controller.GetEntries(pillarKind: PillarKind.Prevent);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = ok.Value.Should().BeAssignableTo<IReadOnlyList<PlaybookEntryResponse>>().Subject;
        entries.Should().ContainSingle().Which.ProposalKind.Should().Be("DriftFinding");
    }

    [Fact]
    public async Task GetEntries_NamespaceFilter_OnlyReturnsMatchingNamespace()
    {
        var namespaceId = Guid.NewGuid();
        await ProposeEntryAsync(OwnerA, namespaceId: namespaceId);
        await ProposeEntryAsync(OwnerA, namespaceId: Guid.NewGuid());

        var result = await _controller.GetEntries(namespaceId: namespaceId);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entries = ok.Value.Should().BeAssignableTo<IReadOnlyList<PlaybookEntryResponse>>().Subject;
        entries.Should().ContainSingle().Which.NamespaceId.Should().Be(namespaceId);
    }

    // ── GetEntryById ────────────────────────────────────────────────

    [Fact]
    public async Task GetEntryById_OwnedEntry_ReturnsEntryWithEvents()
    {
        var entryId = await ProposeEntryAsync(OwnerA);

        var result = await _controller.GetEntryById(entryId);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var detail = ok.Value.Should().BeOfType<PlaybookEntryDetailResponse>().Subject;
        detail.Entry.Id.Should().Be(entryId);
        detail.Events.Should().ContainSingle(e => e.EventType == "Proposed");
    }

    [Fact]
    public async Task GetEntryById_AnotherOwnersEntry_ReturnsNotFound()
    {
        var entryId = await ProposeEntryAsync(OwnerB);

        var result = await _controller.GetEntryById(entryId);

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetEntryById_NonExistentEntry_ReturnsNotFound()
    {
        var result = await _controller.GetEntryById(Guid.NewGuid());

        result.Result.Should().BeOfType<NotFoundResult>();
    }

    // ── MarkUnderReview ─────────────────────────────────────────────

    [Fact]
    public async Task MarkUnderReview_FromProposed_Succeeds()
    {
        var entryId = await ProposeEntryAsync(OwnerA);

        var result = await _controller.MarkUnderReview(entryId);

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entry = ok.Value.Should().BeOfType<PlaybookEntryResponse>().Subject;
        entry.State.Should().Be("UnderReview");
    }

    [Fact]
    public async Task MarkUnderReview_AlreadyUnderReview_ReturnsConflict()
    {
        var entryId = await ProposeEntryAsync(OwnerA);
        await _controller.MarkUnderReview(entryId);

        var result = await _controller.MarkUnderReview(entryId);

        result.Result.Should().BeOfType<ConflictObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status409Conflict);
    }

    // ── Disposition ─────────────────────────────────────────────────

    [Fact]
    public async Task Disposition_Approve_Succeeds()
    {
        var entryId = await ProposeEntryAsync(OwnerA);

        var result = await _controller.Disposition(entryId, new DispositionPlaybookEntryRequest(PlaybookDisposition.Approved, null));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entry = ok.Value.Should().BeOfType<PlaybookEntryResponse>().Subject;
        entry.State.Should().Be("Approved");
        entry.Disposition.Should().Be("Approved");
    }

    [Fact]
    public async Task Disposition_RejectWithoutReason_ReturnsBadRequest()
    {
        var entryId = await ProposeEntryAsync(OwnerA);

        var result = await _controller.Disposition(entryId, new DispositionPlaybookEntryRequest(PlaybookDisposition.Rejected, null));

        result.Result.Should().BeOfType<BadRequestObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Disposition_RejectWithReason_Succeeds()
    {
        var entryId = await ProposeEntryAsync(OwnerA);

        var result = await _controller.Disposition(entryId, new DispositionPlaybookEntryRequest(PlaybookDisposition.Rejected, "not credible"));

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var entry = ok.Value.Should().BeOfType<PlaybookEntryResponse>().Subject;
        entry.State.Should().Be("Rejected");
    }

    [Fact]
    public async Task Disposition_AnotherOwnersEntry_ReturnsNotFound()
    {
        var entryId = await ProposeEntryAsync(OwnerB);

        var result = await _controller.Disposition(entryId, new DispositionPlaybookEntryRequest(PlaybookDisposition.Approved, null));

        result.Result.Should().BeOfType<NotFoundObjectResult>();
    }

    // ── VerifyChain ─────────────────────────────────────────────────

    [Fact]
    public async Task VerifyChain_ValidChain_ReportsValid()
    {
        await ProposeEntryAsync(OwnerA);

        var result = await _controller.VerifyChain();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var verification = ok.Value.Should().BeOfType<ChainVerificationResult>().Subject;
        verification.IsValid.Should().BeTrue();
    }

    // ── GetCorrelationAccountability ───────────────────────────────

    [Fact]
    public async Task GetCorrelationAccountability_NoHypotheses_ReportsZerosAndNullApprovalRate()
    {
        var result = await _controller.GetCorrelationAccountability();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var report = ok.Value.Should().BeOfType<CorrelationAccountabilityReport>().Subject;
        report.TotalHypotheses.Should().Be(0);
        report.ApprovalRate.Should().BeNull();
    }

    [Fact]
    public async Task GetCorrelationAccountability_MixOfDispositions_ComputesApprovalRateOverTerminalOnly()
    {
        var approvedId = await ProposeEntryAsync(OwnerA, pillarKind: PillarKind.Correlate, proposalKind: "CorrelationHypothesis");
        var rejectedId = await ProposeEntryAsync(OwnerA, pillarKind: PillarKind.Correlate, proposalKind: "CorrelationHypothesis");
        await ProposeEntryAsync(OwnerA, pillarKind: PillarKind.Correlate, proposalKind: "CorrelationHypothesis"); // left Proposed
        await ProposeEntryAsync(OwnerA, pillarKind: PillarKind.Investigate, proposalKind: "AnomalyFlag"); // different pillar, excluded
        await ProposeEntryAsync(OwnerB, pillarKind: PillarKind.Correlate, proposalKind: "CorrelationHypothesis"); // different owner, excluded

        await _controller.Disposition(approvedId, new DispositionPlaybookEntryRequest(PlaybookDisposition.Approved, null));
        await _controller.Disposition(rejectedId, new DispositionPlaybookEntryRequest(PlaybookDisposition.Rejected, "not credible"));

        var result = await _controller.GetCorrelationAccountability();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var report = ok.Value.Should().BeOfType<CorrelationAccountabilityReport>().Subject;
        report.TotalHypotheses.Should().Be(3);
        report.ApprovedCount.Should().Be(1);
        report.RejectedCount.Should().Be(1);
        report.ProposedCount.Should().Be(1);
        report.ApprovalRate.Should().Be(0.5);
    }
}
