using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.PlaybookLedger;

namespace ServiceHub.UnitTests.Infrastructure.PlaybookLedger;

public sealed class PlaybookLedgerServiceTests : IDisposable
{
    private const string OwnerId = "owner-a";
    private static readonly PlaybookActor Worker = new("System:AnomalyDetectionWorker", PlaybookActorKind.System);
    private static readonly PlaybookActor Human = new("alex@contoso.com", PlaybookActorKind.User);

    private readonly DlqDbContext _dbContext;
    private readonly PlaybookLedgerService _service;

    public PlaybookLedgerServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new PlaybookLedgerService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static ProposePlaybookEntryRequest BuildProposeRequest(
        string ownerId = OwnerId, PillarKind pillarKind = PillarKind.Investigate, PlaybookActor? proposer = null) =>
        new()
        {
            OwnerId = ownerId,
            PillarKind = pillarKind,
            ProposalKind = "AnomalyFlag",
            EvidenceRefJson = """{"anomalyId":"abc-123"}""",
            ProposalJson = """{"severity":"high"}""",
            Proposer = proposer ?? Worker,
            ExpiresAfter = TimeSpan.FromDays(7),
        };

    [Fact]
    public async Task ProposeAsync_CreatesEntryInProposedState_WithOneProposedEvent()
    {
        var result = await _service.ProposeAsync(BuildProposeRequest());

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(PlaybookEntryState.Proposed);
        result.Value.Disposition.Should().BeNull();
        result.Value.LastEventSeq.Should().Be(1);

        var events = (await _service.GetEventsForEntryAsync(result.Value.Id, OwnerId)).Value;
        events.Should().ContainSingle(e => e.EventType == PlaybookEventType.Proposed && e.Seq == 1);
        events[0].PrevHash.Should().Be(PlaybookHashChain.GenesisHash);
    }

    [Fact]
    public async Task ProposeAsync_RedactsEvidenceAndProposalJson()
    {
        var request = BuildProposeRequest() with
        {
            EvidenceRefJson = """{"connectionString":"Endpoint=sb://leak.servicebus.windows.net/;SharedAccessKey=SECRETVALUE"}""",
        };

        var result = await _service.ProposeAsync(request);

        result.Value.EvidenceRefJson.Should().NotContain("SECRETVALUE");
    }

    [Fact]
    public async Task MarkUnderReviewAsync_FromProposed_Succeeds()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;

        var result = await _service.MarkUnderReviewAsync(entry.Id, OwnerId, Human);

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(PlaybookEntryState.UnderReview);
        result.Value.LastEventSeq.Should().Be(2);
    }

    [Fact]
    public async Task MarkUnderReviewAsync_FromApproved_Fails()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;
        await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);

        var result = await _service.MarkUnderReviewAsync(entry.Id, OwnerId, Human);

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ReviseAsync_FromProposed_SetsEditedState_PreservesOriginalProposalJson()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;
        var originalProposalJson = entry.ProposalJson;

        var result = await _service.ReviseAsync(entry.Id, OwnerId, Human, """{"severity":"medium"}""");

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(PlaybookEntryState.Edited);
        result.Value.ProposalJson.Should().Be(originalProposalJson, "the immutable identity block never changes — the revision lives on the event");

        var events = (await _service.GetEventsForEntryAsync(entry.Id, OwnerId)).Value;
        events.Should().ContainSingle(e => e.EventType == PlaybookEventType.Revised && e.DetailJson!.Contains("medium"));
    }

    [Fact]
    public async Task ReviseAsync_FromRejected_Fails()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;
        await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Rejected, "not applicable");

        var result = await _service.ReviseAsync(entry.Id, OwnerId, Human, "{}");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task DispositionAsync_Approve_SetsApprovedStateAndDisposition_ClosesEntry()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;

        var result = await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(PlaybookEntryState.Approved);
        result.Value.Disposition.Should().Be(PlaybookDisposition.Approved);
        result.Value.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task DispositionAsync_RejectWithoutReason_ReturnsValidationError()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;

        var result = await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Rejected, reason: null);

        result.IsFailure.Should().BeTrue();
        var reloaded = await _dbContext.PlaybookEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        reloaded.State.Should().Be(PlaybookEntryState.Proposed, "a rejected-without-reason attempt must not mutate the entry");
    }

    [Fact]
    public async Task DispositionAsync_RejectWithReason_Succeeds()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;

        var result = await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Rejected, "stale finding");

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(PlaybookEntryState.Rejected);
        result.Value.Disposition.Should().Be(PlaybookDisposition.Rejected);

        var events = (await _service.GetEventsForEntryAsync(entry.Id, OwnerId)).Value;
        events.Should().ContainSingle(e => e.EventType == PlaybookEventType.Rejected && e.DetailJson == "stale finding");
    }

    [Fact]
    public async Task DispositionAsync_AfterEdit_Succeeds()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;
        await _service.ReviseAsync(entry.Id, OwnerId, Human, """{"severity":"medium"}""");

        var result = await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(PlaybookEntryState.Approved);
    }

    [Fact]
    public async Task DispositionAsync_AlreadyTerminal_Fails()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;
        await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);

        var result = await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Rejected, "changed my mind");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ExpireAsync_NonTerminalEntry_SetsExpiredState()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;

        var result = await _service.ExpireAsync(entry.Id, OwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(PlaybookEntryState.Expired);
        result.Value.Disposition.Should().BeNull("expiry is terminal but not a human decision");
    }

    [Fact]
    public async Task ExpireAsync_AlreadyTerminal_IsIdempotentNoOp()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;
        await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);

        var result = await _service.ExpireAsync(entry.Id, OwnerId);

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(PlaybookEntryState.Approved, "expiry must never overwrite an existing terminal decision");

        var events = (await _service.GetEventsForEntryAsync(entry.Id, OwnerId)).Value;
        events.Should().NotContain(e => e.EventType == PlaybookEventType.Expired);
    }

    [Fact]
    public async Task SupersedeAsync_FromProposed_Succeeds_RecordsSupersedingEntryId()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;
        var newer = (await _service.ProposeAsync(BuildProposeRequest())).Value;

        var result = await _service.SupersedeAsync(entry.Id, OwnerId, Worker, newer.Id);

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(PlaybookEntryState.Superseded);

        var events = (await _service.GetEventsForEntryAsync(entry.Id, OwnerId)).Value;
        events.Should().ContainSingle(e => e.EventType == PlaybookEventType.Superseded && e.DetailJson!.Contains(newer.Id.ToString()));
    }

    [Fact]
    public async Task QueryEntriesAsync_FiltersByPillarAndState()
    {
        await _service.ProposeAsync(BuildProposeRequest(pillarKind: PillarKind.Investigate));
        var preventEntry = (await _service.ProposeAsync(BuildProposeRequest(pillarKind: PillarKind.Prevent))).Value;
        await _service.DispositionAsync(preventEntry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);

        var proposedInvestigate = await _service.QueryEntriesAsync(OwnerId, pillarKind: PillarKind.Investigate, state: PlaybookEntryState.Proposed);
        proposedInvestigate.Value.Should().ContainSingle();

        var approvedPrevent = await _service.QueryEntriesAsync(OwnerId, pillarKind: PillarKind.Prevent, state: PlaybookEntryState.Approved);
        approvedPrevent.Value.Should().ContainSingle(e => e.Id == preventEntry.Id);
    }

    [Fact]
    public async Task VerifyChainAsync_AfterMultipleOperations_IsValid()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest())).Value;
        await _service.MarkUnderReviewAsync(entry.Id, OwnerId, Human);
        await _service.ReviseAsync(entry.Id, OwnerId, Human, "{}");
        await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);

        var result = await _service.VerifyChainAsync(OwnerId);

        result.IsValid.Should().BeTrue();
        result.EventsChecked.Should().Be(4);
    }

    [Fact]
    public async Task DifferentOwners_HaveIndependentSeqSpaces()
    {
        var entryA = (await _service.ProposeAsync(BuildProposeRequest(ownerId: "owner-a"))).Value;
        var entryB = (await _service.ProposeAsync(BuildProposeRequest(ownerId: "owner-b"))).Value;

        entryA.LastEventSeq.Should().Be(1);
        entryB.LastEventSeq.Should().Be(1, "each owner's chain starts its own Seq space at 1");
    }
}
