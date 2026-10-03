using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.PlaybookLedger;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.PlaybookLedger;

/// <summary>
/// P5's "Observe/Evaluate" step (PREVENTION-RULE-DESIGN-2026-08-29.md §1/§4/§8/§9/§11). Covers
/// condition matching, the reconciliation invariant ("never more than one Approved version per
/// rule lineage"), occurrence counting, and the expiry sweep — the four pieces of behavior this
/// service adds that no existing test already exercises.
/// </summary>
public sealed class PreventionRuleEvaluationServiceTests : IDisposable
{
    private const string OwnerId = "owner-a";
    private static readonly PlaybookActor Human = new("alex@contoso.com", PlaybookActorKind.User);

    private readonly DlqDbContext _dbContext;
    private readonly IPlaybookLedger _ledger;
    private readonly PreventionRuleEvaluationService _service;
    private readonly Namespace _namespace;
    private Guid NamespaceId => _namespace.Id;

    public PreventionRuleEvaluationServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _ledger = new PlaybookLedgerService(_dbContext);
        _service = new PreventionRuleEvaluationService(_ledger, NullLogger<PreventionRuleEvaluationService>.Instance);

        _namespace = Namespace.Create(
            "orders-ns",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=testkey123456789=",
            environment: EnvironmentType.Dev,
            provider: CloudProviderType.Azure,
            ownerId: OwnerId).Value;
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private async Task<PlaybookEntry> ProposeAndApproveRuleAsync(
        string entityName = "orders-queue",
        string driftFindingType = "SchemaShapeDrift",
        int minSeverity = 70,
        int minOccurrences = 1,
        int windowHours = 24,
        Guid? ruleLineageId = null,
        int ruleVersion = 1,
        DateTimeOffset? ruleExpiresAt = null)
    {
        var rule = new PreventionRuleProposal(
            RuleLineageId: ruleLineageId ?? Guid.NewGuid(),
            RuleVersion: ruleVersion,
            Name: "Test rule",
            EntityName: entityName,
            Condition: new PreventionRuleCondition(driftFindingType, minSeverity, minOccurrences, windowHours),
            SupersedesRuleEntryId: null,
            RuleExpiresAt: ruleExpiresAt ?? DateTimeOffset.UtcNow.AddDays(30),
            Action: PreventionRuleActions.ObserveOnly);

        var proposed = (await _ledger.ProposeAsync(new ProposePlaybookEntryRequest
        {
            OwnerId = OwnerId,
            PillarKind = PillarKind.Prevent,
            ProposalKind = "PreventionRuleProposal",
            EvidenceRefJson = "{}",
            ProposalJson = JsonSerializer.Serialize(rule),
            Proposer = Human,
            NamespaceId = NamespaceId,
            ExpiresAfter = TimeSpan.FromDays(14),
        })).Value;

        return (await _ledger.DispositionAsync(proposed.Id, OwnerId, Human, PlaybookDisposition.Approved, null)).Value;
    }

    private DriftFinding BuildFinding(
        string entityName = "orders-queue", DriftFindingType type = DriftFindingType.SchemaShapeDrift, int severity = 80) =>
        DriftFinding.Create(NamespaceId, entityName, type, severity, "Test finding");

    [Fact]
    public async Task EvaluateAsync_MatchingFinding_WritesPreventionTrigger()
    {
        await ProposeAndApproveRuleAsync();

        await _service.EvaluateAsync(_namespace, new[] { BuildFinding() });

        var entries = (await _ledger.QueryEntriesAsync(OwnerId, PillarKind.Prevent)).Value;
        entries.Should().ContainSingle(e => e.ProposalKind == "PreventionTrigger");
    }

    [Fact]
    public async Task EvaluateAsync_EntityNameMismatch_WritesNothing()
    {
        await ProposeAndApproveRuleAsync(entityName: "orders-queue");

        await _service.EvaluateAsync(_namespace, new[] { BuildFinding(entityName: "payments-queue") });

        var entries = (await _ledger.QueryEntriesAsync(OwnerId, PillarKind.Prevent)).Value;
        entries.Should().NotContain(e => e.ProposalKind == "PreventionTrigger");
    }

    [Fact]
    public async Task EvaluateAsync_SeverityBelowThreshold_WritesNothing()
    {
        await ProposeAndApproveRuleAsync(minSeverity: 70);

        await _service.EvaluateAsync(_namespace, new[] { BuildFinding(severity: 50) });

        var entries = (await _ledger.QueryEntriesAsync(OwnerId, PillarKind.Prevent)).Value;
        entries.Should().NotContain(e => e.ProposalKind == "PreventionTrigger");
    }

    [Fact]
    public async Task EvaluateAsync_DriftFindingTypeMismatch_WritesNothing()
    {
        await ProposeAndApproveRuleAsync(driftFindingType: nameof(DriftFindingType.PayloadFormatDrift));

        await _service.EvaluateAsync(_namespace, new[] { BuildFinding(type: DriftFindingType.SchemaShapeDrift) });

        var entries = (await _ledger.QueryEntriesAsync(OwnerId, PillarKind.Prevent)).Value;
        entries.Should().NotContain(e => e.ProposalKind == "PreventionTrigger");
    }

    [Fact]
    public async Task EvaluateAsync_AnyDriftFindingType_MatchesEveryType()
    {
        await ProposeAndApproveRuleAsync(driftFindingType: "Any");

        await _service.EvaluateAsync(_namespace, new[] { BuildFinding(type: DriftFindingType.PayloadFormatDrift) });

        var entries = (await _ledger.QueryEntriesAsync(OwnerId, PillarKind.Prevent)).Value;
        entries.Should().ContainSingle(e => e.ProposalKind == "PreventionTrigger");
    }

    [Fact]
    public async Task EvaluateAsync_RevokedRule_NeverMatches()
    {
        var ruleEntry = await ProposeAndApproveRuleAsync();
        await _ledger.RevokeAsync(ruleEntry.Id, OwnerId, Human, "Turned off for the test.");

        await _service.EvaluateAsync(_namespace, new[] { BuildFinding() });

        var entries = (await _ledger.QueryEntriesAsync(OwnerId, PillarKind.Prevent)).Value;
        entries.Should().NotContain(e => e.ProposalKind == "PreventionTrigger");
    }

    [Fact]
    public async Task EvaluateAsync_TriggerCarriesEntityNameForBacktesting()
    {
        await ProposeAndApproveRuleAsync(entityName: "orders-queue");

        await _service.EvaluateAsync(_namespace, new[] { BuildFinding(entityName: "orders-queue") });

        var trigger = (await _ledger.QueryEntriesAsync(OwnerId, PillarKind.Prevent)).Value
            .Single(e => e.ProposalKind == "PreventionTrigger");

        using var doc = JsonDocument.Parse(trigger.ProposalJson);
        doc.RootElement.GetProperty("EntityName").GetString().Should().Be("orders-queue");
    }

    [Fact]
    public async Task EvaluateAsync_RepeatedMatchesWithinWindow_IncrementOccurrenceCount()
    {
        await ProposeAndApproveRuleAsync(minOccurrences: 2, windowHours: 24);

        await _service.EvaluateAsync(_namespace, new[] { BuildFinding() });
        await _service.EvaluateAsync(_namespace, new[] { BuildFinding() });

        var triggers = (await _ledger.QueryEntriesAsync(OwnerId, PillarKind.Prevent)).Value
            .Where(e => e.ProposalKind == "PreventionTrigger")
            .OrderBy(e => e.ProposedAt)
            .ToList();

        triggers.Should().HaveCount(2);

        var first = JsonSerializer.Deserialize<PreventionTriggerProposal>(triggers[0].ProposalJson)!;
        var second = JsonSerializer.Deserialize<PreventionTriggerProposal>(triggers[1].ProposalJson)!;

        first.OccurrencesInWindow.Should().Be(1);
        first.MetOccurrenceThreshold.Should().BeFalse();
        second.OccurrencesInWindow.Should().Be(2);
        second.MetOccurrenceThreshold.Should().BeTrue();
    }

    [Fact]
    public async Task GetActiveRulesAsync_TwoApprovedVersionsOfSameLineage_ReturnsNewestOnly_ButNeverRevokesTheStaleOne()
    {
        // GetActiveRulesAsync is a pure read (fixed after hostile review: it used to revoke the
        // stale version as a side effect, which meant a PlaybookRead-scoped caller could trigger a
        // governed ledger mutation merely by calling GET /prevention-rules/active). Reconciliation
        // now happens only via EvaluateAsync's system-authored path — see the test below.
        var lineageId = Guid.NewGuid();
        var v1 = await ProposeAndApproveRuleAsync(ruleLineageId: lineageId, ruleVersion: 1);
        var v2 = await ProposeAndApproveRuleAsync(ruleLineageId: lineageId, ruleVersion: 2);

        var active = await _service.GetActiveRulesAsync(OwnerId, NamespaceId);

        active.Should().ContainSingle(e => e.Id == v2.Id);

        var reloadedV1 = await _ledger.GetEntryAsync(v1.Id, OwnerId);
        reloadedV1!.State.Should().Be(PlaybookEntryState.Approved, "a pure read must never mutate ledger state");
    }

    [Fact]
    public async Task EvaluateAsync_TwoApprovedVersionsOfSameLineage_ReconcilesByRevokingStaleVersion()
    {
        var lineageId = Guid.NewGuid();
        var v1 = await ProposeAndApproveRuleAsync(ruleLineageId: lineageId, ruleVersion: 1);
        await ProposeAndApproveRuleAsync(ruleLineageId: lineageId, ruleVersion: 2);

        await _service.EvaluateAsync(_namespace, new[] { BuildFinding() });

        var reloadedV1 = await _ledger.GetEntryAsync(v1.Id, OwnerId);
        reloadedV1!.State.Should().Be(PlaybookEntryState.Revoked);
    }

    [Fact]
    public async Task SweepExpiredRulesAsync_PastRuleExpiresAt_RevokesRule()
    {
        var ruleEntry = await ProposeAndApproveRuleAsync(ruleExpiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));

        var revokedCount = await _service.SweepExpiredRulesAsync(OwnerId, DateTimeOffset.UtcNow);

        revokedCount.Should().Be(1);
        var reloaded = await _ledger.GetEntryAsync(ruleEntry.Id, OwnerId);
        reloaded!.State.Should().Be(PlaybookEntryState.Revoked);
    }

    [Fact]
    public async Task SweepExpiredRulesAsync_NotYetExpired_LeavesRuleApproved()
    {
        var ruleEntry = await ProposeAndApproveRuleAsync(ruleExpiresAt: DateTimeOffset.UtcNow.AddDays(30));

        var revokedCount = await _service.SweepExpiredRulesAsync(OwnerId, DateTimeOffset.UtcNow);

        revokedCount.Should().Be(0);
        var reloaded = await _ledger.GetEntryAsync(ruleEntry.Id, OwnerId);
        reloaded!.State.Should().Be(PlaybookEntryState.Approved);
    }

    // Hostile-review fix: CountPriorTriggers used to re-query the namespace's full Prevent-pillar
    // entry set once per rule×finding match, growing with every trigger already written. A mocked
    // ledger is used here (rather than the real one above) so the query count itself — not just
    // the resulting data — can be asserted.
    [Fact]
    public async Task EvaluateAsync_MultipleMatchesInOneCycle_QueriesExistingTriggersOnlyOnce()
    {
        var ledgerMock = new Mock<IPlaybookLedger>();
        var service = new PreventionRuleEvaluationService(ledgerMock.Object, NullLogger<PreventionRuleEvaluationService>.Instance);

        var rule1 = new PreventionRuleProposal(
            Guid.NewGuid(), 1, "Rule 1", "queue-a", new PreventionRuleCondition("Any", 0, 1, 24), null, DateTimeOffset.UtcNow.AddDays(30), PreventionRuleActions.ObserveOnly);
        var rule2 = new PreventionRuleProposal(
            Guid.NewGuid(), 1, "Rule 2", "queue-b", new PreventionRuleCondition("Any", 0, 1, 24), null, DateTimeOffset.UtcNow.AddDays(30), PreventionRuleActions.ObserveOnly);

        PlaybookEntry BuildRuleEntry(PreventionRuleProposal rule) => new()
        {
            OwnerId = OwnerId,
            PillarKind = PillarKind.Prevent,
            ProposalKind = "PreventionRuleProposal",
            EvidenceRefJson = "{}",
            ProposalJson = JsonSerializer.Serialize(rule),
            ProposedAt = DateTimeOffset.UtcNow,
            ProposerIdentity = "alex@contoso.com",
            ProposerKind = PlaybookActorKind.User,
            NamespaceId = NamespaceId,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(14),
            State = PlaybookEntryState.Approved,
            Disposition = PlaybookDisposition.Approved,
        };

        ledgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Prevent, NamespaceId, PlaybookEntryState.Approved, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success<IReadOnlyList<PlaybookEntry>>(new[] { BuildRuleEntry(rule1), BuildRuleEntry(rule2) }));

        var existingTriggerQueryCount = 0;
        ledgerMock
            .Setup(l => l.QueryEntriesAsync(OwnerId, PillarKind.Prevent, NamespaceId, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                existingTriggerQueryCount++;
                return Result.Success<IReadOnlyList<PlaybookEntry>>(Array.Empty<PlaybookEntry>());
            });

        ledgerMock
            .Setup(l => l.ProposeAsync(It.IsAny<ProposePlaybookEntryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(BuildRuleEntry(rule1)));

        var findings = new[]
        {
            BuildFinding(entityName: "queue-a"),
            BuildFinding(entityName: "queue-b"),
        };

        await service.EvaluateAsync(_namespace, findings);

        existingTriggerQueryCount.Should().Be(1, "the existing-trigger snapshot must be loaded once per cycle, not once per rule×finding match");
    }
}
