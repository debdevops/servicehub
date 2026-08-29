using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.PlaybookLedger;
using ServiceHub.Shared.Constants;

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

    // P5 (PREVENTION-RULE-DESIGN-2026-08-29.md §9): RevokeAsync is the one narrow ledger addition
    // this design needs — turning off a standing, already-Approved construct. These tests cover
    // its two safety properties: only a ProposalKind on the revocable allow-list, and only from
    // Approved, ever transitions to Revoked.
    [Fact]
    public async Task RevokeAsync_ApprovedPreventionRuleProposal_SetsRevokedState()
    {
        var entry = (await _service.ProposeAsync(
            BuildProposeRequest(pillarKind: PillarKind.Prevent) with { ProposalKind = "PreventionRuleProposal" })).Value;
        await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);

        var result = await _service.RevokeAsync(entry.Id, OwnerId, Human, "No longer needed.");

        result.IsSuccess.Should().BeTrue();
        result.Value.State.Should().Be(PlaybookEntryState.Revoked);
        result.Value.ClosedAt.Should().NotBeNull();

        var events = (await _service.GetEventsForEntryAsync(entry.Id, OwnerId)).Value;
        events.Should().ContainSingle(e => e.EventType == PlaybookEventType.Revoked && e.DetailJson == "No longer needed.");
    }

    [Fact]
    public async Task RevokeAsync_WithoutReason_ReturnsValidationError()
    {
        var entry = (await _service.ProposeAsync(
            BuildProposeRequest(pillarKind: PillarKind.Prevent) with { ProposalKind = "PreventionRuleProposal" })).Value;
        await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);

        var result = await _service.RevokeAsync(entry.Id, OwnerId, Human, "   ");

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(ErrorCodes.Playbook.ReasonRequired);
    }

    [Fact]
    public async Task RevokeAsync_NonRevocableProposalKind_Fails_EvenWhenApproved()
    {
        // ReplayPlan's Approved state means "a human agreed this was sound" — a permanent
        // historical fact, never a standing construct that can be turned off.
        var entry = (await _service.ProposeAsync(
            BuildProposeRequest(pillarKind: PillarKind.Recover) with { ProposalKind = "ReplayPlan" })).Value;
        await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);

        var result = await _service.RevokeAsync(entry.Id, OwnerId, Human, "Trying to undo an approval.");

        result.IsFailure.Should().BeTrue();

        var reloaded = await _service.GetEntryAsync(entry.Id, OwnerId);
        reloaded!.State.Should().Be(PlaybookEntryState.Approved, "a non-revocable ProposalKind's Approved state must never change");
    }

    [Fact]
    public async Task RevokeAsync_FromProposed_Fails()
    {
        var entry = (await _service.ProposeAsync(
            BuildProposeRequest(pillarKind: PillarKind.Prevent) with { ProposalKind = "PreventionRuleProposal" })).Value;

        var result = await _service.RevokeAsync(entry.Id, OwnerId, Human, "Too early.");

        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAsync_AlreadyRevoked_Fails_NotIdempotent()
    {
        // Deliberately distinct from ExpireAsync's idempotent-no-op contract: revocation is an
        // explicit, reasoned operator action, not a background sweep that may see the same row
        // more than once, so a second revoke attempt is a real conflict, not a silent success.
        var entry = (await _service.ProposeAsync(
            BuildProposeRequest(pillarKind: PillarKind.Prevent) with { ProposalKind = "PreventionRuleProposal" })).Value;
        await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);
        await _service.RevokeAsync(entry.Id, OwnerId, Human, "First revoke.");

        var result = await _service.RevokeAsync(entry.Id, OwnerId, Human, "Second revoke.");

        result.IsFailure.Should().BeTrue();
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

    // ── GetDueForExpiryAsync ────────────────────────────────────────

    [Fact]
    public async Task GetDueForExpiryAsync_ExpiredNonTerminalEntry_IsReturned()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest() with { ExpiresAfter = TimeSpan.FromDays(-1) })).Value;

        var due = await _service.GetDueForExpiryAsync(OwnerId, DateTimeOffset.UtcNow);

        due.Value.Should().ContainSingle(e => e.Id == entry.Id);
    }

    [Fact]
    public async Task GetDueForExpiryAsync_NotYetExpired_IsExcluded()
    {
        await _service.ProposeAsync(BuildProposeRequest() with { ExpiresAfter = TimeSpan.FromDays(7) });

        var due = await _service.GetDueForExpiryAsync(OwnerId, DateTimeOffset.UtcNow);

        due.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueForExpiryAsync_AlreadyTerminalEntry_IsExcludedEvenIfExpired()
    {
        var entry = (await _service.ProposeAsync(BuildProposeRequest() with { ExpiresAfter = TimeSpan.FromDays(-1) })).Value;
        await _service.DispositionAsync(entry.Id, OwnerId, Human, PlaybookDisposition.Approved, null);

        var due = await _service.GetDueForExpiryAsync(OwnerId, DateTimeOffset.UtcNow);

        due.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueForExpiryAsync_AnotherOwnersExpiredEntry_IsExcluded()
    {
        await _service.ProposeAsync(BuildProposeRequest(ownerId: "owner-b") with { ExpiresAfter = TimeSpan.FromDays(-1) });

        var due = await _service.GetDueForExpiryAsync(OwnerId, DateTimeOffset.UtcNow);

        due.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDueForExpiryAsync_OrdersOldestExpiryFirst_AndRespectsLimit()
    {
        var older = (await _service.ProposeAsync(BuildProposeRequest() with { ExpiresAfter = TimeSpan.FromDays(-3) })).Value;
        var newer = (await _service.ProposeAsync(BuildProposeRequest() with { ExpiresAfter = TimeSpan.FromDays(-1) })).Value;

        var due = await _service.GetDueForExpiryAsync(OwnerId, DateTimeOffset.UtcNow, limit: 1);

        due.Value.Should().ContainSingle(e => e.Id == older.Id);
        due.Value.Should().NotContain(e => e.Id == newer.Id);
    }

    [Fact]
    public async Task GetDueForExpiryAsync_UnderReviewAndEdited_AreBothConsideredNonTerminal()
    {
        var underReview = (await _service.ProposeAsync(BuildProposeRequest() with { ExpiresAfter = TimeSpan.FromDays(-1) })).Value;
        await _service.MarkUnderReviewAsync(underReview.Id, OwnerId, Human);

        var edited = (await _service.ProposeAsync(BuildProposeRequest() with { ExpiresAfter = TimeSpan.FromDays(-1) })).Value;
        await _service.ReviseAsync(edited.Id, OwnerId, Human, "{}");

        var due = await _service.GetDueForExpiryAsync(OwnerId, DateTimeOffset.UtcNow);

        due.Value.Should().HaveCount(2);
        due.Value.Should().Contain(e => e.Id == underReview.Id);
        due.Value.Should().Contain(e => e.Id == edited.Id);
    }
}
