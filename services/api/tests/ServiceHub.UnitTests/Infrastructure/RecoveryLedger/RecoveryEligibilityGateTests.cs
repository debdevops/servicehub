using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

/// <summary>
/// Predicate-matrix tests for <see cref="RecoveryEligibilityGate"/> (roadmap §9/Phase B) — one
/// per predicate, plus ordering and fail-closed behavior. <see cref="AutoReplayExecutorTests"/>'s
/// existing recurrence/rate-limit tests (unchanged assertions, now passing against the gate)
/// separately cover the "bit-for-bit unchanged" regression requirement for that call site.
/// </summary>
public sealed class RecoveryEligibilityGateTests : IDisposable
{
    private const string OwnerId = "entra:test-owner-123";
    private static readonly Guid NamespaceId = Guid.NewGuid();

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _ledger;
    private readonly RecoveryEligibilityGate _gate;

    public RecoveryEligibilityGateTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _ledger = new RecoveryLedgerService(_dbContext);
        _gate = new RecoveryEligibilityGate(_ledger, NullLogger<RecoveryEligibilityGate>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecoveryEligibilityRequest BuildRequest(
        RecoveryOperationKind actionKind = RecoveryOperationKind.Replay,
        RecoveryActorKind actorKind = RecoveryActorKind.User,
        string? entityName = "orders",
        string? bodyHash = "hash-1",
        string? signatureHash = null,
        EnvironmentType? environment = EnvironmentType.Dev,
        bool rateLimitExceeded = false) =>
        new(OwnerId, actionKind, actorKind, RecoveryTrigger.Manual, NamespaceId, entityName, bodyHash,
            signatureHash, environment, rateLimitExceeded);

    private void SeedLineageEntry(
        string entityName,
        string bodyHash,
        DateTimeOffset begunAt,
        bool markerApplied = true,
        VerificationConfidence? confidence = null,
        string? signatureHash = null)
    {
        var operation = new RecoveryOperation
        {
            OwnerId = OwnerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.AutoRule,
            ActorIdentity = "test-rule",
            ActorKind = RecoveryActorKind.Automation,
            ScopeDescription = "test",
            ServiceVersion = "test",
            OpenedAt = begunAt,
            TargetCount = 1,
        };
        _dbContext.RecoveryOperations.Add(operation);

        var entry = new RecoveryLedgerEntry
        {
            OperationId = operation.Id,
            OwnerId = OwnerId,
            NamespaceId = NamespaceId,
            EntityNameSnapshot = entityName,
            BodyHash = bodyHash,
            TargetEntity = entityName,
            BegunAt = begunAt,
            State = RecoveryEntryState.Recovered,
            MarkerApplied = markerApplied,
            VerificationConfidence = confidence,
            SignatureHashSnapshot = signatureHash,
            // Distinct provider identity per seeded entry — the lineage match must never depend
            // on these fields (roadmap: provider MessageId/SequenceNumber are never recovery identity).
            SourceMessageIdSnapshot = $"provider-msg-{Guid.NewGuid():N}",
            SourceSequenceNumberSnapshot = Random.Shared.NextInt64(),
        };
        _dbContext.RecoveryLedgerEntries.Add(entry);
        _dbContext.SaveChanges();
    }

    // ── Predicate 1: purge origin (§9.1) ─────────────────────────────────────

    [Theory]
    [InlineData(RecoveryActorKind.Automation)]
    [InlineData(RecoveryActorKind.System)]
    public async Task Purge_AutomationOrSystemActor_Denied(RecoveryActorKind actorKind)
    {
        var decision = await _gate.EvaluateAsync(BuildRequest(RecoveryOperationKind.Purge, actorKind));

        decision.Verdict.Should().Be(EligibilityVerdict.Deny);
        decision.ReasonCode.Should().Be("PURGE_AUTOMATION_PROHIBITED");
    }

    [Theory]
    [InlineData(RecoveryActorKind.User)]
    [InlineData(RecoveryActorKind.ApiKey)]
    public async Task Purge_HumanActor_NotDeniedByPredicate1(RecoveryActorKind actorKind)
    {
        var decision = await _gate.EvaluateAsync(BuildRequest(RecoveryOperationKind.Purge, actorKind));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task Replay_AutomationActor_NotDeniedByPredicate1()
    {
        // Predicate 1 targets Purge only — Automation may still replay (AutoReplayExecutor).
        var decision = await _gate.EvaluateAsync(BuildRequest(RecoveryOperationKind.Replay, RecoveryActorKind.Automation));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    // ── Predicate 2: production elevation (§9) ───────────────────────────────

    [Fact]
    public async Task ProductionEnvironment_Denied()
    {
        var decision = await _gate.EvaluateAsync(BuildRequest(environment: EnvironmentType.Prod));

        decision.Verdict.Should().Be(EligibilityVerdict.Deny);
        decision.ReasonCode.Should().Be("PRODUCTION_ELEVATION_REQUIRED");
    }

    [Fact]
    public async Task NonProductionEnvironment_NotDeniedByPredicate2()
    {
        var decision = await _gate.EvaluateAsync(BuildRequest(environment: EnvironmentType.Dev));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    // ── Predicate 3: recurrence cap (§7.5), actor-conditional per accepted Option B ─────────────

    [Theory]
    [InlineData(RecoveryActorKind.Automation)]
    [InlineData(RecoveryActorKind.System)]
    [InlineData(RecoveryActorKind.User)]
    [InlineData(RecoveryActorKind.ApiKey)]
    public async Task FewerThanThreePriorMatches_NotBlockedForAnyActor(RecoveryActorKind actorKind)
    {
        SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-1), confidence: VerificationConfidence.Exact);
        SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-2), confidence: VerificationConfidence.Exact);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: actorKind));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Theory]
    [InlineData(RecoveryActorKind.Automation)]
    [InlineData(RecoveryActorKind.System)]
    public async Task ThreePriorExactConfidenceMatches_AutomationOrSystem_EscalatesWithMatchedCount(RecoveryActorKind actorKind)
    {
        for (var i = 0; i < 3; i++)
            SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-i - 1), confidence: VerificationConfidence.Exact);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: actorKind));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("RECURRENCE_CAP_EXCEEDED");
        decision.MatchedCount.Should().Be(3);
    }

    [Theory]
    [InlineData(RecoveryActorKind.User)]
    [InlineData(RecoveryActorKind.ApiKey)]
    public async Task ThreePriorExactConfidenceMatches_HumanActor_NotDeniedByPredicate3(RecoveryActorKind actorKind)
    {
        // Accepted Option B: a human hitting the recurrence cap is not auto-denied by predicate
        // 3 — the gate continues (predicates 4/5 pass here too), so the overall verdict is Allow
        // and the human's recovery is recorded as its own real outcome, not a fabricated Declined.
        for (var i = 0; i < 3; i++)
            SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-i - 1), confidence: VerificationConfidence.Exact);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: actorKind));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task ThreePriorHeuristicConfidenceMatches_AutomationActor_EscalatesWithHeuristicReason()
    {
        for (var i = 0; i < 3; i++)
            SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-i - 1),
                confidence: VerificationConfidence.Heuristic, signatureHash: "sig-1");

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("RECURRENCE_CAP_EXCEEDED_HEURISTIC");
    }

    [Fact]
    public async Task ThreePriorMatchesWithDivergentSignaturesAndUncorroboratedEntry_AutomationActor_EscalatesAsAmbiguousCollision()
    {
        SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-1),
            markerApplied: false, confidence: VerificationConfidence.Heuristic, signatureHash: "sig-a");
        SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-2),
            confidence: VerificationConfidence.Exact, signatureHash: "sig-b");
        SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-3),
            confidence: VerificationConfidence.Exact, signatureHash: "sig-b");

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("RECURRENCE_CAP_AMBIGUOUS_COLLISION");
    }

    [Fact]
    public async Task NoRealMessageIdentity_SkipsPredicate3()
    {
        // No BodyHash (e.g. an untracked message) can't be lineage-checked — must never be
        // treated as a match against other requests sharing the same null-standing-in value.
        var decision = await _gate.EvaluateAsync(BuildRequest(bodyHash: null));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task LineageQueryThrows_FailsClosedToEscalate()
    {
        var ledgerMock = new Mock<IRecoveryLedger>();
        ledgerMock
            .Setup(l => l.FindLineageMatchesAsync(
                It.IsAny<string>(), It.IsAny<Guid?>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated query failure"));
        var gate = new RecoveryEligibilityGate(ledgerMock.Object, NullLogger<RecoveryEligibilityGate>.Instance);

        var decision = await gate.EvaluateAsync(BuildRequest());

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("RECURRENCE_CAP_QUERY_ERROR");
    }

    // ── Predicate 4: rate limit ────────────────────────────────────────────────

    [Fact]
    public async Task RateLimitExceeded_Escalates()
    {
        var decision = await _gate.EvaluateAsync(BuildRequest(rateLimitExceeded: true));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be(RecoveryEligibilityGate.ReasonRateLimited);
    }

    [Fact]
    public async Task RateLimitNotExceeded_NotDeniedByPredicate4()
    {
        var decision = await _gate.EvaluateAsync(BuildRequest(rateLimitExceeded: false));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    // ── Predicate 5: autonomy lookup — log-only per §29.6 until Phase D ─────────

    [Fact]
    public async Task AutomationActorWithNoSignatureHash_StillAllowedBecausePredicate5IsLogOnly()
    {
        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: null));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task AutomationActorWithSignatureHash_StillAllowedBecausePredicate5IsLogOnly()
    {
        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    // ── Predicate ordering ───────────────────────────────────────────────────

    [Fact]
    public async Task PurgeAutomationInProduction_Predicate1FiresBeforePredicate2()
    {
        var decision = await _gate.EvaluateAsync(BuildRequest(
            RecoveryOperationKind.Purge, RecoveryActorKind.Automation, environment: EnvironmentType.Prod));

        decision.ReasonCode.Should().Be("PURGE_AUTOMATION_PROHIBITED");
    }

    [Fact]
    public async Task RecurrenceCapAndRateLimitBothFire_AutomationActor_Predicate3FiresBeforePredicate4()
    {
        for (var i = 0; i < 3; i++)
            SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-i - 1), confidence: VerificationConfidence.Exact);

        var decision = await _gate.EvaluateAsync(
            BuildRequest(actorKind: RecoveryActorKind.Automation, rateLimitExceeded: true));

        decision.ReasonCode.Should().Be("RECURRENCE_CAP_EXCEEDED");
    }

    [Fact]
    public async Task RecurrenceCapReachedByHumanActor_AnotherPredicateStillDenies()
    {
        // Case 6: predicate 3 doesn't block a human at the cap, but the gate keeps evaluating —
        // a subsequent hard predicate (here, rate limit) can still deny/escalate normally.
        for (var i = 0; i < 3; i++)
            SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-i - 1), confidence: VerificationConfidence.Exact);

        var decision = await _gate.EvaluateAsync(
            BuildRequest(actorKind: RecoveryActorKind.User, rateLimitExceeded: true));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be(RecoveryEligibilityGate.ReasonRateLimited);
    }
}
