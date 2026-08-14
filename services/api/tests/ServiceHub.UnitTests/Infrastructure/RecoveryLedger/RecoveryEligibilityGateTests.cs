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

    // ── Predicate 0: emergency stop (§9.4.2, §15.2) ──────────────────────────

    [Theory]
    [InlineData(RecoveryActorKind.Automation)]
    [InlineData(RecoveryActorKind.System)]
    public async Task EmergencyStopActive_AutomationOrSystemActor_Escalates(RecoveryActorKind actorKind)
    {
        await _ledger.RecordEmergencyControlEventAsync(OwnerId, new RecoveryActor("admin", RecoveryActorKind.User), activate: true, reason: null);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: actorKind));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("EMERGENCY_STOP_ACTIVE");
    }

    [Theory]
    [InlineData(RecoveryActorKind.User)]
    [InlineData(RecoveryActorKind.ApiKey)]
    public async Task EmergencyStopActive_HumanActor_NeverBlockedByPredicate0(RecoveryActorKind actorKind)
    {
        await _ledger.RecordEmergencyControlEventAsync(OwnerId, new RecoveryActor("admin", RecoveryActorKind.User), activate: true, reason: null);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: actorKind));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task EmergencyStopNeverActivated_AutomationActor_NotBlockedByPredicate0()
    {
        await SeedGrantAsync("sig-1", AutonomyLevel.Standing);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task EmergencyStopActivatedThenCleared_AutomationActor_NoLongerBlocked()
    {
        await SeedGrantAsync("sig-1", AutonomyLevel.Standing);
        await _ledger.RecordEmergencyControlEventAsync(OwnerId, new RecoveryActor("admin", RecoveryActorKind.User), activate: true, reason: null);
        await _ledger.RecordEmergencyControlEventAsync(OwnerId, new RecoveryActor("admin", RecoveryActorKind.User), activate: false, reason: null);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task EmergencyStopActiveForDifferentOwner_DoesNotBlockThisOwnersAutomation()
    {
        await SeedGrantAsync("sig-1", AutonomyLevel.Standing);
        await _ledger.RecordEmergencyControlEventAsync(
            "a-different-owner", new RecoveryActor("admin", RecoveryActorKind.User), activate: true, reason: null);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task EmergencyStopQueryThrows_AutomationActor_FailsClosedToEscalate()
    {
        var ledgerMock = new Mock<IRecoveryLedger>();
        ledgerMock
            .Setup(l => l.IsEmergencyStopActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated query failure"));
        var gate = new RecoveryEligibilityGate(ledgerMock.Object, NullLogger<RecoveryEligibilityGate>.Instance);

        var decision = await gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("EMERGENCY_STOP_QUERY_ERROR");
    }

    [Fact]
    public async Task EmergencyStopQueryThrows_HumanActor_NeverQueriedAndStillAllowed()
    {
        // Predicate 0 must skip the read entirely for User/ApiKey — "never affects manual
        // recovery" must hold regardless of whether the emergency-stop query itself is healthy.
        var ledgerMock = new Mock<IRecoveryLedger>();
        ledgerMock
            .Setup(l => l.IsEmergencyStopActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated query failure"));
        var gate = new RecoveryEligibilityGate(ledgerMock.Object, NullLogger<RecoveryEligibilityGate>.Instance);

        var decision = await gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.User, bodyHash: null));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
        ledgerMock.Verify(
            l => l.IsEmergencyStopActiveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EmergencyStopActiveAndPurgeAttempted_AutomationActor_Predicate0FiresBeforePredicate1()
    {
        await _ledger.RecordEmergencyControlEventAsync(OwnerId, new RecoveryActor("admin", RecoveryActorKind.User), activate: true, reason: null);

        var decision = await _gate.EvaluateAsync(
            BuildRequest(RecoveryOperationKind.Purge, RecoveryActorKind.Automation));

        decision.ReasonCode.Should().Be("EMERGENCY_STOP_ACTIVE");
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
        await SeedGrantAsync("sig-1", AutonomyLevel.Standing);

        var decision = await _gate.EvaluateAsync(
            BuildRequest(RecoveryOperationKind.Replay, RecoveryActorKind.Automation, signatureHash: "sig-1"));

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
        // Harmless for the non-Automation cases: predicate 5 only reads this grant for Automation.
        await SeedGrantAsync("sig-1", AutonomyLevel.Standing);
        SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-1), confidence: VerificationConfidence.Exact);
        SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-2), confidence: VerificationConfidence.Exact);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: actorKind, signatureHash: "sig-1"));

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

    // ── Predicate 5: autonomy lookup — enforced per §9.4.3 ──────────────────────

    private async Task SeedGrantAsync(string signatureHash, AutonomyLevel level, string ownerId = OwnerId)
    {
        await _ledger.RecordAutonomyGrantTransitionAsync(
            ownerId, signatureHash, RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, level, "test", evidenceJson: null);
    }

    [Fact]
    public async Task AutomationActor_NullSignatureHash_Escalates()
    {
        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: null));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("AUTONOMY_SIGNATURE_HASH_MISSING");
    }

    [Fact]
    public async Task AutomationActor_NoGrantForSignature_Escalates()
    {
        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("AUTONOMY_GRANT_INSUFFICIENT");
    }

    [Fact]
    public async Task AutomationActor_GrantAtL3Approve_Escalates()
    {
        // L3 is the permanent human-approved floor — a row does not need to exist for a signature
        // to be "at L3"; this proves an explicit Approve row (e.g. after a demotion) escalates the
        // same as no row at all.
        await SeedGrantAsync("sig-1", AutonomyLevel.Standing);
        await _ledger.RecordAutonomyGrantTransitionAsync(
            OwnerId, "sig-1", RecoveryOperationKind.Replay,
            AutonomyLevel.Standing, AutonomyLevel.Approve, "demoted for test", evidenceJson: null);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("AUTONOMY_GRANT_INSUFFICIENT");
    }

    [Fact]
    public async Task AutomationActor_GrantAtL4Standing_Allows()
    {
        await SeedGrantAsync("sig-1", AutonomyLevel.Standing);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task AutomationActor_GrantAtL5Unattended_Allows()
    {
        await SeedGrantAsync("sig-1", AutonomyLevel.Unattended);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task AutomationActor_GrantExistsForDifferentActionKind_Escalates()
    {
        // The grant lookup is scoped to (OwnerId, SignatureHash, ActionKind) — a Standing grant
        // earned for Replay must never authorize a Purge attempt under the same signature.
        await _ledger.RecordAutonomyGrantTransitionAsync(
            OwnerId, "sig-1", RecoveryOperationKind.Purge,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "test", evidenceJson: null);

        var decision = await _gate.EvaluateAsync(BuildRequest(
            actionKind: RecoveryOperationKind.Replay, actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("AUTONOMY_GRANT_INSUFFICIENT");
    }

    [Fact]
    public async Task AutomationActor_GrantExistsForDifferentOwner_Escalates()
    {
        await SeedGrantAsync("sig-1", AutonomyLevel.Standing, ownerId: "entra:a-different-owner");

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("AUTONOMY_GRANT_INSUFFICIENT");
    }

    [Theory]
    [InlineData(RecoveryActorKind.User)]
    [InlineData(RecoveryActorKind.ApiKey)]
    [InlineData(RecoveryActorKind.System)]
    public async Task NonAutomationActor_NoGrantAndNoSignatureHash_NeverBlockedByPredicate5(RecoveryActorKind actorKind)
    {
        // A human (or System) is never subject to predicate 5 — only unattended Automation needs
        // an earned grant. L3 (manual/human recovery) must keep working exactly as today.
        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: actorKind, signatureHash: null));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task AutomationActor_GrantQueryThrows_FailsClosedToEscalate()
    {
        var ledgerMock = new Mock<IRecoveryLedger>();
        ledgerMock
            .Setup(l => l.GetAutonomyGrantAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<RecoveryOperationKind>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated query failure"));
        var gate = new RecoveryEligibilityGate(ledgerMock.Object, NullLogger<RecoveryEligibilityGate>.Instance);

        // bodyHash: null skips predicate 3's lineage query, which this mock never configured —
        // this test is isolating predicate 5's own fail-closed behavior only.
        var decision = await gate.EvaluateAsync(
            BuildRequest(actorKind: RecoveryActorKind.Automation, bodyHash: null, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("AUTONOMY_GRANT_QUERY_ERROR");
    }

    [Fact]
    public async Task AutomationActor_EmergencyStopActive_Predicate0FiresBeforePredicate5EvenWithValidGrant()
    {
        await SeedGrantAsync("sig-1", AutonomyLevel.Unattended);
        await _ledger.RecordEmergencyControlEventAsync(OwnerId, new RecoveryActor("admin", RecoveryActorKind.User), activate: true, reason: null);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("EMERGENCY_STOP_ACTIVE");
    }

    [Fact]
    public async Task AutomationActor_RecurrenceCapExceeded_Predicate3FiresBeforePredicate5EvenWithValidGrant()
    {
        await SeedGrantAsync("sig-1", AutonomyLevel.Unattended);
        for (var i = 0; i < 3; i++)
            SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-i - 1), confidence: VerificationConfidence.Exact);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be("RECURRENCE_CAP_EXCEEDED");
    }

    [Fact]
    public async Task AutomationActor_RateLimitExceeded_Predicate4FiresBeforePredicate5EvenWithValidGrant()
    {
        await SeedGrantAsync("sig-1", AutonomyLevel.Unattended);

        var decision = await _gate.EvaluateAsync(BuildRequest(
            actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1", rateLimitExceeded: true));

        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be(RecoveryEligibilityGate.ReasonRateLimited);
    }

    [Fact]
    public async Task AutomationActor_ValidGrantAndHumanRecurrenceContextNotApplicable_PlainAllow()
    {
        // Predicate 5 passing must still surface as a plain Allow (no leftover ReasonCode) when
        // there is no recurrence context to carry — Automation never reaches Option B's carried
        // context since predicate 3 hard-stops it before predicate 5 is ever evaluated.
        await SeedGrantAsync("sig-1", AutonomyLevel.Standing);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.Automation, signatureHash: "sig-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
        decision.ReasonCode.Should().BeNull();
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

    // ── Predicate 3 human-recurrence observability (roadmap §9.4.1) ────────────

    [Theory]
    [InlineData(RecoveryActorKind.User)]
    [InlineData(RecoveryActorKind.ApiKey)]
    public async Task RecurrenceCapReachedByHumanActor_AllowedButCarriesReasonCodeAndMatchedCount(
        RecoveryActorKind actorKind)
    {
        for (var i = 0; i < 3; i++)
            SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-i - 1), confidence: VerificationConfidence.Exact);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: actorKind));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
        decision.ReasonCode.Should().Be("RECURRENCE_CAP_EXCEEDED");
        decision.MatchedCount.Should().Be(3);
    }

    [Fact]
    public async Task RecurrenceCapNotReachedByHumanActor_PlainAllowWithNoReasonCode()
    {
        SeedLineageEntry("orders", "hash-1", DateTimeOffset.UtcNow.AddDays(-1), confidence: VerificationConfidence.Exact);

        var decision = await _gate.EvaluateAsync(BuildRequest(actorKind: RecoveryActorKind.User));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
        decision.ReasonCode.Should().BeNull();
        decision.MatchedCount.Should().Be(0);
    }
}
