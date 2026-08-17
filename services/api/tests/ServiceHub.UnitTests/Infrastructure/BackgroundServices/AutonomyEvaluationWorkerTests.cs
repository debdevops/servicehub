using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.BackgroundServices;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

/// <summary>
/// Phase D coverage for <see cref="AutonomyEvaluationWorker"/>: the sweep finds every signature
/// with replay evidence, evaluates it, and — as of this increment — writes the resulting
/// promotion/demotion to <c>AutonomyGrant</c> whenever the evidence genuinely earns or forfeits
/// standing. Predicate 5 enforcement and <c>AutoReplayExecutor</c>'s <c>SignatureHash</c> gap are
/// explicitly out of scope for this increment/this test file.
/// </summary>
public sealed class AutonomyEvaluationWorkerTests : IDisposable
{
    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _recoveryLedger;
    private readonly RecoveryTrustScoringService _trustScoring;

    public AutonomyEvaluationWorkerTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _recoveryLedger = new RecoveryLedgerService(_dbContext);
        _trustScoring = new RecoveryTrustScoringService(_recoveryLedger);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecoveryActor Actor(RecoveryActorKind kind = RecoveryActorKind.User) => new("test-actor", kind);

    // IPlatformEventBus is resolved from the root provider in the constructor (mirrors
    // DlqMonitorWorker's existing convention for the same dependency) — must be present even
    // when a test never trips the circuit breaker, or construction itself throws.
    private static AutonomyEvaluationWorker CreateWorker(IDictionary<string, string?>? config = null)
    {
        var rootServices = new ServiceCollection();
        rootServices.AddSingleton(Mock.Of<IPlatformEventBus>());
        return new(
            rootServices.BuildServiceProvider(),
            new ConfigurationBuilder().AddInMemoryCollection(config ?? new Dictionary<string, string?>()).Build(),
            NullLogger<AutonomyEvaluationWorker>.Instance);
    }

    private IServiceProvider BuildScope()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRecoveryLedger>(_recoveryLedger);
        services.AddSingleton<IRecoveryTrustScoringService>(_trustScoring);
        // SweepOwnerAsync's circuit-breaker step (SweepAutoReplayCircuitBreakersAsync) resolves
        // DlqDbContext from this scope to enumerate the owner's AutoReplayRules.
        services.AddSingleton(_dbContext);
        return services.BuildServiceProvider();
    }

    private async Task<RecoveryOperation> OpenOperationAsync(string ownerId, RecoveryOperationKind kind = RecoveryOperationKind.Replay)
    {
        var result = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = kind,
            Trigger = RecoveryTrigger.Manual,
            Actor = Actor(),
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    /// <summary>Drives one Replay entry to a terminal disposition end-to-end — mirrors
    /// <c>RecoveryTrustScoringServiceTests</c>'s helper of the same shape. Defaults to Azure —
    /// most of these tests exercise trust-score math, not provider gating (roadmap §14, Phase F);
    /// pass <paramref name="provider"/> explicitly for the provider-guard tests below.</summary>
    private async Task<Guid> CreateReplayEntryAsync(
        string ownerId, string signatureHash, RecoveryDisposition disposition, string bodyHash,
        CloudProviderType provider = CloudProviderType.Azure)
    {
        var operation = await OpenOperationAsync(ownerId);
        var entryResult = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = ownerId,
            Actor = Actor(),
            BodyHash = bodyHash,
            SignatureHashSnapshot = signatureHash,
            TargetEntity = "orders-dlq",
            ProviderSnapshot = provider,
        });
        entryResult.IsSuccess.Should().BeTrue();
        var entry = entryResult.Value;

        if (disposition == RecoveryDisposition.Failed)
        {
            var rejected = await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
            {
                EntryId = entry.Id,
                OwnerId = ownerId,
                Actor = Actor(),
                Outcome = RecoveryExecutionOutcome.Rejected,
            });
            rejected.IsSuccess.Should().BeTrue();
            return entry.Id;
        }

        var accepted = await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = ownerId,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });
        accepted.IsSuccess.Should().BeTrue();

        var outcome = disposition == RecoveryDisposition.Returned
            ? RecoveryObservationOutcome.RecurrenceObserved
            : RecoveryObservationOutcome.NoRecurrenceObserved;

        var observed = await _recoveryLedger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = ownerId,
            Actor = new RecoveryActor("verification-worker", RecoveryActorKind.System),
            Outcome = outcome,
            Confidence = outcome == RecoveryObservationOutcome.RecurrenceObserved ? VerificationConfidence.Exact : null,
        });
        observed.IsSuccess.Should().BeTrue();
        return entry.Id;
    }

    /// <summary>Seeds <paramref name="count"/> Recovered entries for one signature — the
    /// cheapest way to build a clean L4-eligible (n≥10) or L5-eligible (n≥30) sample.</summary>
    private async Task SeedRecoveredEntriesAsync(
        string ownerId, string signatureHash, int count, string prefix,
        CloudProviderType provider = CloudProviderType.Azure)
    {
        for (var i = 0; i < count; i++)
        {
            await CreateReplayEntryAsync(ownerId, signatureHash, RecoveryDisposition.Recovered, $"{prefix}-{i}", provider);
        }
    }

    // ── Promotion: L3 → L4 ──────────────────────────────────────────────────

    [Fact]
    public async Task SweepOwnerAsync_InsufficientEvidence_WritesNoGrant()
    {
        await SeedRecoveredEntriesAsync(OwnerA, "sig-thin", count: 3, "body");

        var entryCountBefore = await _dbContext.RecoveryLedgerEntries.CountAsync();
        var eventCountBefore = await _dbContext.RecoveryEvents.CountAsync();

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        (await _dbContext.AutonomyGrants.AnyAsync(g => g.SignatureHash == "sig-thin")).Should().BeFalse(
            "insufficient evidence (n=3 < 10) must never create an L4/L5 grant — roadmap §8.9");
        (await _dbContext.RecoveryLedgerEntries.CountAsync()).Should().Be(entryCountBefore);
        (await _dbContext.RecoveryEvents.CountAsync()).Should().Be(eventCountBefore);
    }

    [Fact]
    public async Task SweepOwnerAsync_MeetsL4SampleAndRate_PromotesToStandingWithForensicEvent()
    {
        await SeedRecoveredEntriesAsync(OwnerA, "sig-good", count: 10, "body");

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        var grant = await _dbContext.AutonomyGrants.SingleAsync(g => g.SignatureHash == "sig-good");
        grant.OwnerId.Should().Be(OwnerA);
        grant.ActionKind.Should().Be(RecoveryOperationKind.Replay);
        grant.CurrentLevel.Should().Be(AutonomyLevel.Standing);

        var promoted = await _dbContext.RecoveryEvents
            .SingleAsync(e => e.EventType == RecoveryEventType.AutonomyGrantPromoted);
        promoted.DetailJson.Should().Contain("sig-good").And.Contain("\"newLevel\":\"Standing\"");
    }

    [Fact]
    public async Task SweepOwnerAsync_MeetsL4ButUnsafeFlagPresent_DoesNotPromote()
    {
        var flaggedEntryId = await CreateReplayEntryAsync(OwnerA, "sig-unsafe", RecoveryDisposition.Recovered, "flagged");
        await SeedRecoveredEntriesAsync(OwnerA, "sig-unsafe", count: 9, "body");

        var flagResult = await _recoveryLedger.RecordOutcomeFlagAsync(
            flaggedEntryId, OwnerA, Actor(), RecoveryOutcomeFlagKind.Unsafe, "customer reported data loss");
        flagResult.IsSuccess.Should().BeTrue();

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        (await _dbContext.AutonomyGrants.AnyAsync(g => g.SignatureHash == "sig-unsafe")).Should().BeFalse(
            "an unsafe-outcome flag withholds L4/L5 fleet-wide even with n/rate satisfied — roadmap §8.10");
    }

    [Fact]
    public async Task SweepOwnerAsync_MeetsL4ButDuplicateFlagPresent_DoesNotPromote()
    {
        var flaggedEntryId = await CreateReplayEntryAsync(OwnerA, "sig-dup", RecoveryDisposition.Recovered, "flagged");
        await SeedRecoveredEntriesAsync(OwnerA, "sig-dup", count: 9, "body");

        var flagResult = await _recoveryLedger.RecordOutcomeFlagAsync(
            flaggedEntryId, OwnerA, Actor(), RecoveryOutcomeFlagKind.DuplicateBusinessEffect, "double-charged customer");
        flagResult.IsSuccess.Should().BeTrue();

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        (await _dbContext.AutonomyGrants.AnyAsync(g => g.SignatureHash == "sig-dup")).Should().BeFalse(
            "duplicate_association permanently disqualifies this signature — roadmap §8.10");
    }

    [Fact]
    public async Task SweepOwnerAsync_OwnerIsolation_OnlyEvaluatesRequestedOwnersEvidence()
    {
        await SeedRecoveredEntriesAsync(OwnerA, "shared-sig", count: 10, "a-body");
        await SeedRecoveredEntriesAsync(OwnerB, "shared-sig", count: 3, "b-body");

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);
        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerB, CancellationToken.None);

        var grantA = await _dbContext.AutonomyGrants.SingleAsync(g => g.OwnerId == OwnerA && g.SignatureHash == "shared-sig");
        grantA.CurrentLevel.Should().Be(AutonomyLevel.Standing);

        (await _dbContext.AutonomyGrants.AnyAsync(g => g.OwnerId == OwnerB && g.SignatureHash == "shared-sig"))
            .Should().BeFalse("owner B's own evidence (n=3) never earns a grant, and owner A's evidence must never leak across");
    }

    // ── Promotion: L4 → L5, one tier per sweep ─────────────────────────────

    [Fact]
    public async Task SweepOwnerAsync_AlreadyAtStandingWithL5Evidence_PromotesToUnattended()
    {
        await SeedRecoveredEntriesAsync(OwnerA, "sig-veteran", count: 30, "body");
        var seed = await _recoveryLedger.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-veteran", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "seeded at L4 for this test", null);
        seed.IsSuccess.Should().BeTrue();

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        var grant = await _dbContext.AutonomyGrants.SingleAsync(g => g.SignatureHash == "sig-veteran");
        grant.CurrentLevel.Should().Be(AutonomyLevel.Unattended);

        (await _dbContext.RecoveryEvents.CountAsync(e => e.EventType == RecoveryEventType.AutonomyGrantPromoted))
            .Should().Be(2, "one seeded L3→L4 event plus this sweep's L4→L5 event");
    }

    [Fact]
    public async Task SweepOwnerAsync_L3WithL5QualityEvidence_PromotesOnlyOneTierPerSweep()
    {
        // A signature that happens to already have n≥30/99%+ evidence but has never been
        // granted L4 must earn L4 first, not jump straight to L5 in one sweep.
        await SeedRecoveredEntriesAsync(OwnerA, "sig-jumper", count: 30, "body");

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        var grant = await _dbContext.AutonomyGrants.SingleAsync(g => g.SignatureHash == "sig-jumper");
        grant.CurrentLevel.Should().Be(AutonomyLevel.Standing, "L3→L4 and L4→L5 are separate, sequential transitions");

        // The next sweep, now that the grant is at L4, completes the second transition.
        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        var grantAfterSecondSweep = await _dbContext.AutonomyGrants.SingleAsync(g => g.SignatureHash == "sig-jumper");
        grantAfterSecondSweep.CurrentLevel.Should().Be(AutonomyLevel.Unattended);
    }

    // ── Demotion ────────────────────────────────────────────────────────────

    [Fact]
    public async Task SweepOwnerAsync_StandingGrantWithRateBelowFloor_DemotesToApprove()
    {
        var seed = await _recoveryLedger.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-slipping", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "seeded at L4 for this test", null);
        seed.IsSuccess.Should().BeTrue();

        // 9/10 = 90% < the 95% floor required to hold L4.
        await SeedRecoveredEntriesAsync(OwnerA, "sig-slipping", count: 9, "ok");
        await CreateReplayEntryAsync(OwnerA, "sig-slipping", RecoveryDisposition.Returned, "bad-1");

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        var grant = await _dbContext.AutonomyGrants.SingleAsync(g => g.SignatureHash == "sig-slipping");
        grant.CurrentLevel.Should().Be(AutonomyLevel.Approve, "a verified success rate below 95% is a non-disableable L4→L3 demotion — roadmap §8.5");

        (await _dbContext.RecoveryEvents.CountAsync(e => e.EventType == RecoveryEventType.AutonomyGrantDemoted)).Should().Be(1);
    }

    [Fact]
    public async Task SweepOwnerAsync_StandingGrantWithDuplicateFlag_DemotesToApprove()
    {
        var seed = await _recoveryLedger.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-duped", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "seeded at L4 for this test", null);
        seed.IsSuccess.Should().BeTrue();

        var flaggedEntryId = await CreateReplayEntryAsync(OwnerA, "sig-duped", RecoveryDisposition.Recovered, "flagged");
        await SeedRecoveredEntriesAsync(OwnerA, "sig-duped", count: 9, "ok");

        var flagResult = await _recoveryLedger.RecordOutcomeFlagAsync(
            flaggedEntryId, OwnerA, Actor(), RecoveryOutcomeFlagKind.DuplicateBusinessEffect, "double-charged customer");
        flagResult.IsSuccess.Should().BeTrue();

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        var grant = await _dbContext.AutonomyGrants.SingleAsync(g => g.SignatureHash == "sig-duped");
        grant.CurrentLevel.Should().Be(AutonomyLevel.Approve,
            "duplicate_association is a permanent L4/L5 disqualifier, restorable only by an explicit human act — roadmap §8.10");
    }

    [Fact]
    public async Task SweepOwnerAsync_UnattendedGrantWithDuplicateFlag_DemotesToApprove()
    {
        var seed = await _recoveryLedger.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-l5-duped", RecoveryOperationKind.Replay,
            AutonomyLevel.Standing, AutonomyLevel.Unattended, "seeded at L5 for this test", null);
        seed.IsSuccess.Should().BeTrue();

        var flaggedEntryId = await CreateReplayEntryAsync(OwnerA, "sig-l5-duped", RecoveryDisposition.Recovered, "flagged");
        await SeedRecoveredEntriesAsync(OwnerA, "sig-l5-duped", count: 29, "ok");

        var flagResult = await _recoveryLedger.RecordOutcomeFlagAsync(
            flaggedEntryId, OwnerA, Actor(), RecoveryOutcomeFlagKind.DuplicateBusinessEffect, "double-charged customer");
        flagResult.IsSuccess.Should().BeTrue();

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        var grant = await _dbContext.AutonomyGrants.SingleAsync(g => g.SignatureHash == "sig-l5-duped");
        grant.CurrentLevel.Should().Be(AutonomyLevel.Approve, "duplicate_association demotes L5 straight to L3, same as L4 — roadmap §8.6");
    }

    [Fact]
    public async Task SweepOwnerAsync_UnattendedGrantWithRateBelowNinetyNinePercent_DoesNotAutoDemote()
    {
        // §8.6/§8.7 define no rate-based demotion trigger for L5 in this document — only
        // duplicate_association and the (not-yet-implemented) consecutive-failure check. A rate
        // drop alone must not demote an L5 grant this increment.
        var seed = await _recoveryLedger.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-l5-slip", RecoveryOperationKind.Replay,
            AutonomyLevel.Standing, AutonomyLevel.Unattended, "seeded at L5 for this test", null);
        seed.IsSuccess.Should().BeTrue();

        // 29/30 = ~96.7%, still >= the 95% L4 floor but well below the 99% L5 bar.
        await SeedRecoveredEntriesAsync(OwnerA, "sig-l5-slip", count: 29, "ok");
        await CreateReplayEntryAsync(OwnerA, "sig-l5-slip", RecoveryDisposition.Returned, "bad-1");

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        var grant = await _dbContext.AutonomyGrants.SingleAsync(g => g.SignatureHash == "sig-l5-slip");
        grant.CurrentLevel.Should().Be(AutonomyLevel.Unattended);
        (await _dbContext.RecoveryEvents.CountAsync(e => e.EventType == RecoveryEventType.AutonomyGrantDemoted)).Should().Be(0);
    }

    // ── Concurrency isolation ───────────────────────────────────────────────

    [Fact]
    public async Task SweepOwnerAsync_ConcurrencyRaceOnOneSignature_LogsAndContinuesToTheNextSignature()
    {
        var evidence = new SignatureTrustEvidence(
            OwnerId: OwnerA, SignatureHash: "irrelevant", ActionKind: RecoveryOperationKind.Replay,
            RecoveredCount: 20, ReturnedCount: 0, FailedCount: 0, UnverifiedCount: 0, DeclinedCount: 0,
            SampleSize: 20, VerifiedSuccessRate: 1.0, MeetsL4SampleAndRate: true, MeetsL5SampleAndRate: false,
            UnsafeOutcomePresent: false, DuplicateAssociationPresent: false, Reasons: []);

        var ledgerMock = new Mock<IRecoveryLedger>();
        ledgerMock.Setup(l => l.GetDistinctSignatureHashesAsync(OwnerA, RecoveryOperationKind.Replay, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "sig-racing", "sig-fine" });
        ledgerMock.Setup(l => l.GetAutonomyGrantAsync(OwnerA, It.IsAny<string>(), RecoveryOperationKind.Replay, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AutonomyGrant?)null);
        ledgerMock.Setup(l => l.GetSignatureProviderAsync(OwnerA, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(CloudProviderType.Azure);
        ledgerMock.Setup(l => l.RecordAutonomyGrantTransitionAsync(
                OwnerA, "sig-racing", RecoveryOperationKind.Replay, AutonomyLevel.Approve, AutonomyLevel.Standing,
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new DbUpdateConcurrencyException("lost the concurrency race"));
        ledgerMock.Setup(l => l.RecordAutonomyGrantTransitionAsync(
                OwnerA, "sig-fine", RecoveryOperationKind.Replay, AutonomyLevel.Approve, AutonomyLevel.Standing,
                It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<AutonomyGrant>.Success(new AutonomyGrant
            {
                OwnerId = OwnerA,
                SignatureHash = "sig-fine",
                ActionKind = RecoveryOperationKind.Replay,
                CurrentLevel = AutonomyLevel.Standing,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
            }));

        var trustMock = new Mock<IRecoveryTrustScoringService>();
        trustMock.Setup(t => t.EvaluateAsync(OwnerA, It.IsAny<string>(), RecoveryOperationKind.Replay, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<SignatureTrustEvidence>.Success(evidence));

        var services = new ServiceCollection();
        services.AddSingleton(ledgerMock.Object);
        services.AddSingleton(trustMock.Object);
        // SweepOwnerAsync's circuit-breaker step resolves DlqDbContext to enumerate the owner's
        // AutoReplayRules — none seeded here, so it safely no-ops.
        services.AddSingleton(_dbContext);
        var provider = services.BuildServiceProvider();

        var act = () => CreateWorker().SweepOwnerAsync(provider, OwnerA, CancellationToken.None);
        await act.Should().NotThrowAsync("a losing concurrent writer must be caught and logged, never crash the sweep — roadmap §9.4.4");

        ledgerMock.Verify(l => l.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-fine", RecoveryOperationKind.Replay, AutonomyLevel.Approve, AutonomyLevel.Standing,
            It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once,
            "the signature after the one that lost its race must still be evaluated");
    }

    // ── DetermineTransition — pure logic, every branch (no DB) ─────────────

    private static SignatureTrustEvidence Evidence(
        int sampleSize, double? rate, bool meetsL4, bool meetsL5, bool unsafeOutcome = false, bool duplicate = false) =>
        new(
            OwnerId: OwnerA, SignatureHash: "sig", ActionKind: RecoveryOperationKind.Replay,
            RecoveredCount: sampleSize, ReturnedCount: 0, FailedCount: 0, UnverifiedCount: 0, DeclinedCount: 0,
            SampleSize: sampleSize, VerifiedSuccessRate: rate, MeetsL4SampleAndRate: meetsL4, MeetsL5SampleAndRate: meetsL5,
            UnsafeOutcomePresent: unsafeOutcome, DuplicateAssociationPresent: duplicate, Reasons: []);

    [Fact]
    public void DetermineTransition_ApproveWithInsufficientEvidence_ReturnsNull()
    {
        AutonomyEvaluationWorker.DetermineTransition(AutonomyLevel.Approve, Evidence(3, 1.0, meetsL4: false, meetsL5: false), canProveDlqAbsence: true)
            .Should().BeNull();
    }

    [Fact]
    public void DetermineTransition_ApproveMeetsL4_ReturnsPromotionToStanding()
    {
        var transition = AutonomyEvaluationWorker.DetermineTransition(
            AutonomyLevel.Approve, Evidence(10, 0.95, meetsL4: true, meetsL5: false), canProveDlqAbsence: true);

        transition.Should().NotBeNull();
        transition!.Value.NewLevel.Should().Be(AutonomyLevel.Standing);
    }

    [Fact]
    public void DetermineTransition_ApproveMeetsL4ButUnsafe_ReturnsNull()
    {
        AutonomyEvaluationWorker.DetermineTransition(
                AutonomyLevel.Approve, Evidence(10, 0.95, meetsL4: true, meetsL5: false, unsafeOutcome: true), canProveDlqAbsence: true)
            .Should().BeNull();
    }

    [Fact]
    public void DetermineTransition_ApproveMeetsL4ButDuplicate_ReturnsNull()
    {
        AutonomyEvaluationWorker.DetermineTransition(
                AutonomyLevel.Approve, Evidence(10, 0.95, meetsL4: true, meetsL5: false, duplicate: true), canProveDlqAbsence: true)
            .Should().BeNull();
    }

    [Fact]
    public void DetermineTransition_StandingMeetsL5_ReturnsPromotionToUnattended()
    {
        var transition = AutonomyEvaluationWorker.DetermineTransition(
            AutonomyLevel.Standing, Evidence(30, 0.99, meetsL4: true, meetsL5: true), canProveDlqAbsence: true);

        transition.Should().NotBeNull();
        transition!.Value.NewLevel.Should().Be(AutonomyLevel.Unattended);
    }

    [Fact]
    public void DetermineTransition_StandingBelowL4Floor_ReturnsDemotionToApprove()
    {
        var transition = AutonomyEvaluationWorker.DetermineTransition(
            AutonomyLevel.Standing, Evidence(20, 0.90, meetsL4: false, meetsL5: false), canProveDlqAbsence: true);

        transition.Should().NotBeNull();
        transition!.Value.NewLevel.Should().Be(AutonomyLevel.Approve);
    }

    [Fact]
    public void DetermineTransition_StandingWithDuplicate_ReturnsDemotionToApprove()
    {
        var transition = AutonomyEvaluationWorker.DetermineTransition(
            AutonomyLevel.Standing, Evidence(50, 1.0, meetsL4: true, meetsL5: true, duplicate: true), canProveDlqAbsence: true);

        transition.Should().NotBeNull();
        transition!.Value.NewLevel.Should().Be(AutonomyLevel.Approve);
    }

    [Fact]
    public void DetermineTransition_UnattendedWithDuplicate_ReturnsDemotionToApprove()
    {
        var transition = AutonomyEvaluationWorker.DetermineTransition(
            AutonomyLevel.Unattended, Evidence(50, 1.0, meetsL4: true, meetsL5: true, duplicate: true), canProveDlqAbsence: true);

        transition.Should().NotBeNull();
        transition!.Value.NewLevel.Should().Be(AutonomyLevel.Approve);
    }

    [Fact]
    public void DetermineTransition_UnattendedBelowL5RateButAboveL4Floor_ReturnsNull()
    {
        // No rate-based demotion trigger exists for L5 in §8.6/§8.7 — only duplicate_association
        // and the (deferred) consecutive-failure check.
        AutonomyEvaluationWorker.DetermineTransition(
                AutonomyLevel.Unattended, Evidence(30, 0.96, meetsL4: true, meetsL5: false), canProveDlqAbsence: true)
            .Should().BeNull();
    }

    [Fact]
    public void DetermineTransition_ApproveWithNoQualifyingEvidenceAtAll_ReturnsNull()
    {
        AutonomyEvaluationWorker.DetermineTransition(AutonomyLevel.Approve, Evidence(0, null, meetsL4: false, meetsL5: false), canProveDlqAbsence: true)
            .Should().BeNull();
    }

    // ── Provider guard (roadmap §14, Phase F) ──────────────────────────────

    [Fact]
    public void DetermineTransition_ApproveMeetsL4ButCannotProveDlqAbsence_ReturnsNull()
    {
        // An AWS/GCP signature with a perfect ratio still cannot promote past L3 under default
        // capabilities — ServiceHub cannot verify what it cannot observe (roadmap §14).
        AutonomyEvaluationWorker.DetermineTransition(
                AutonomyLevel.Approve, Evidence(30, 1.0, meetsL4: true, meetsL5: true), canProveDlqAbsence: false)
            .Should().BeNull();
    }

    [Fact]
    public void DetermineTransition_StandingMeetsL5ButCannotProveDlqAbsence_ReturnsNull()
    {
        AutonomyEvaluationWorker.DetermineTransition(
                AutonomyLevel.Standing, Evidence(50, 1.0, meetsL4: true, meetsL5: true), canProveDlqAbsence: false)
            .Should().BeNull();
    }

    [Fact]
    public void DetermineTransition_StandingBelowL4FloorAndCannotProveDlqAbsence_StillDemotes()
    {
        // Demotion must never be blocked by the provider guard — withdrawing trust is always safe.
        var transition = AutonomyEvaluationWorker.DetermineTransition(
            AutonomyLevel.Standing, Evidence(20, 0.90, meetsL4: false, meetsL5: false), canProveDlqAbsence: false);

        transition.Should().NotBeNull();
        transition!.Value.NewLevel.Should().Be(AutonomyLevel.Approve);
    }

    [Fact]
    public async Task SweepOwnerAsync_AwsSignatureWithThirtyObservationUnavailableOutcomes_NeverPromotesPastApprove()
    {
        // The honest end-to-end path: AWS entries can only close as Returned or
        // ObservationUnavailable (never NoRecurrenceObserved), since CanProveDlqAbsence is false
        // for AWS (RecoveryVerificationWorker.cs) — so they carry zero verified evidence even at
        // volume. This is the trust-scoring exclusion (§8.10) already doing its job; the dedicated
        // provider-guard test below covers the new Phase F defense-in-depth layer on top of it.
        var operation = await OpenOperationAsync(OwnerA);
        for (var i = 0; i < 30; i++)
        {
            var entryResult = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
            {
                OperationId = operation.Id,
                OwnerId = OwnerA,
                Actor = Actor(),
                BodyHash = $"aws-body-{i}",
                SignatureHashSnapshot = "sig-aws-unverified",
                TargetEntity = "orders-dlq",
                ProviderSnapshot = CloudProviderType.Aws,
            });
            entryResult.IsSuccess.Should().BeTrue();
            var entry = entryResult.Value;

            var accepted = await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
            {
                EntryId = entry.Id,
                OwnerId = OwnerA,
                Actor = Actor(),
                Outcome = RecoveryExecutionOutcome.Accepted,
            });
            accepted.IsSuccess.Should().BeTrue();

            var observed = await _recoveryLedger.RecordObservationAsync(new RecordObservationRequest
            {
                EntryId = entry.Id,
                OwnerId = OwnerA,
                Actor = new RecoveryActor("verification-worker", RecoveryActorKind.System),
                Outcome = RecoveryObservationOutcome.ObservationUnavailable,
            });
            observed.IsSuccess.Should().BeTrue();
        }

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        (await _dbContext.AutonomyGrants.AnyAsync(g => g.SignatureHash == "sig-aws-unverified")).Should().BeFalse(
            "even 30 clean observation-unavailable outcomes carry zero verified evidence and must never earn L4");
    }

    [Fact]
    public async Task SweepOwnerAsync_AwsSignatureWithPerfectVerifiedRatio_StillNeverPromotesPastApprove()
    {
        // Adversarial (roadmap §14/Phase F): even if a signature somehow accumulated genuine
        // Recovered dispositions while tagged AWS — evidence the real pipeline cannot produce
        // today, per the test above — the explicit provider guard in DetermineTransition must
        // independently block promotion. Defense-in-depth: this must hold even if the trust-score
        // exclusion that currently makes this scenario unreachable were ever weakened by a future
        // change.
        await SeedRecoveredEntriesAsync(OwnerA, "sig-aws-perfect", count: 30, "aws-body", CloudProviderType.Aws);

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        (await _dbContext.AutonomyGrants.AnyAsync(g => g.SignatureHash == "sig-aws-perfect")).Should().BeFalse(
            "an AWS-tagged signature must never reach L4 regardless of sample size or rate — roadmap §14");
    }

    [Fact]
    public async Task SweepOwnerAsync_GcpSignatureWithPerfectVerifiedRatio_StillNeverPromotesPastApprove()
    {
        await SeedRecoveredEntriesAsync(OwnerA, "sig-gcp-perfect", count: 30, "gcp-body", CloudProviderType.Gcp);

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        (await _dbContext.AutonomyGrants.AnyAsync(g => g.SignatureHash == "sig-gcp-perfect")).Should().BeFalse(
            "a GCP-tagged signature must never reach L4 regardless of sample size or rate — roadmap §14");
    }

    [Fact]
    public async Task SweepOwnerAsync_AzureSignatureWithPerfectVerifiedRatio_PromotesNormally()
    {
        // Positive control: the guard must not affect the one provider that can actually satisfy it.
        await SeedRecoveredEntriesAsync(OwnerA, "sig-azure-perfect", count: 10, "azure-body", CloudProviderType.Azure);

        await CreateWorker().SweepOwnerAsync(BuildScope(), OwnerA, CancellationToken.None);

        var grant = await _dbContext.AutonomyGrants.SingleAsync(g => g.SignatureHash == "sig-azure-perfect");
        grant.CurrentLevel.Should().Be(AutonomyLevel.Standing);
    }

    // ── SweepAutoReplayCircuitBreakersAsync (success-rate circuit breaker) ──────────────────────

    private static IDictionary<string, string?> CircuitBreakerConfig(int sampleSize, double floor) => new Dictionary<string, string?>
    {
        ["RecoveryEvidence:CircuitBreakerSampleSize"] = sampleSize.ToString(),
        ["RecoveryEvidence:CircuitBreakerSuccessRateFloor"] = floor.ToString(System.Globalization.CultureInfo.InvariantCulture),
    };

    private async Task<AutoReplayRule> CreateRuleAsync(string ownerId, string name, bool enabled = true)
    {
        var rule = new AutoReplayRule
        {
            Name = name, OwnerId = ownerId, Enabled = enabled,
            ConditionsJson = "[]", ActionsJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        _dbContext.AutoReplayRules.Add(rule);
        await _dbContext.SaveChangesAsync();
        return rule;
    }

    /// <summary>Seeds one verified (Recovered/Returned) entry attributed to a rule via
    /// <see cref="RecoveryOperation.SourceRuleId"/> — the circuit breaker's source query key.</summary>
    private async Task SeedRuleDispositionAsync(
        string ownerId, long ruleId, RecoveryDisposition disposition, string bodyHash)
    {
        var operation = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId, Kind = RecoveryOperationKind.Replay, Trigger = RecoveryTrigger.AutoRule,
            Actor = Actor(RecoveryActorKind.Automation), ScopeDescription = "entity=orders-dlq",
            SourceRuleId = ruleId, TargetCount = 1,
        });
        var entry = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Value.Id, OwnerId = ownerId, Actor = Actor(RecoveryActorKind.Automation),
            BodyHash = bodyHash, TargetEntity = "orders-dlq",
        });
        await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Value.Id, OwnerId = ownerId, Actor = Actor(RecoveryActorKind.Automation),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });
        var outcome = disposition == RecoveryDisposition.Returned
            ? RecoveryObservationOutcome.RecurrenceObserved
            : RecoveryObservationOutcome.NoRecurrenceObserved;
        await _recoveryLedger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Value.Id, OwnerId = ownerId, Actor = new RecoveryActor("verification-worker", RecoveryActorKind.System),
            Outcome = outcome,
            Confidence = outcome == RecoveryObservationOutcome.RecurrenceObserved ? VerificationConfidence.Exact : null,
        });
    }

    [Fact]
    public async Task SweepAutoReplayCircuitBreakersAsync_BelowSampleSize_DoesNotTrip()
    {
        var rule = await CreateRuleAsync(OwnerA, "Thin Sample Rule");
        for (var i = 0; i < 3; i++)
        {
            await SeedRuleDispositionAsync(OwnerA, rule.Id, RecoveryDisposition.Returned, $"body-{i}");
        }

        var worker = CreateWorker(CircuitBreakerConfig(sampleSize: 4, floor: 0.50));
        await worker.SweepAutoReplayCircuitBreakersAsync(BuildScope(), OwnerA, _recoveryLedger, CancellationToken.None);

        (await _dbContext.AutoReplayRules.SingleAsync(r => r.Id == rule.Id)).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task SweepAutoReplayCircuitBreakersAsync_SuccessRateAtOrAboveFloor_DoesNotTrip()
    {
        var rule = await CreateRuleAsync(OwnerA, "Healthy Rule");
        await SeedRuleDispositionAsync(OwnerA, rule.Id, RecoveryDisposition.Recovered, "body-1");
        await SeedRuleDispositionAsync(OwnerA, rule.Id, RecoveryDisposition.Recovered, "body-2");
        await SeedRuleDispositionAsync(OwnerA, rule.Id, RecoveryDisposition.Returned, "body-3");
        await SeedRuleDispositionAsync(OwnerA, rule.Id, RecoveryDisposition.Returned, "body-4");

        // Exactly 50% verified success — at the floor, not below it.
        var worker = CreateWorker(CircuitBreakerConfig(sampleSize: 4, floor: 0.50));
        await worker.SweepAutoReplayCircuitBreakersAsync(BuildScope(), OwnerA, _recoveryLedger, CancellationToken.None);

        (await _dbContext.AutoReplayRules.SingleAsync(r => r.Id == rule.Id)).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task SweepAutoReplayCircuitBreakersAsync_SuccessRateBelowFloor_DisablesRuleAndWritesLedgerEvent()
    {
        var rule = await CreateRuleAsync(OwnerA, "Poison Message Rule");
        await SeedRuleDispositionAsync(OwnerA, rule.Id, RecoveryDisposition.Returned, "body-1");
        await SeedRuleDispositionAsync(OwnerA, rule.Id, RecoveryDisposition.Returned, "body-2");
        await SeedRuleDispositionAsync(OwnerA, rule.Id, RecoveryDisposition.Returned, "body-3");
        await SeedRuleDispositionAsync(OwnerA, rule.Id, RecoveryDisposition.Recovered, "body-4");

        var worker = CreateWorker(CircuitBreakerConfig(sampleSize: 4, floor: 0.50));
        await worker.SweepAutoReplayCircuitBreakersAsync(BuildScope(), OwnerA, _recoveryLedger, CancellationToken.None);

        (await _dbContext.AutoReplayRules.SingleAsync(r => r.Id == rule.Id)).Enabled.Should().BeFalse();

        var evt = await _dbContext.RecoveryEvents
            .SingleAsync(e => e.EventType == RecoveryEventType.AutoReplayRuleCircuitBreakerTripped);
        evt.DetailJson.Should().Contain("Poison Message Rule");

        var operation = await _dbContext.RecoveryOperations.SingleAsync(o => o.Id == evt.OperationId);
        operation.Kind.Should().Be(RecoveryOperationKind.AutoReplayRuleControl);
        operation.SourceRuleId.Should().Be(rule.Id);
    }

    [Fact]
    public async Task SweepAutoReplayCircuitBreakersAsync_OnlyDisablesTheOffendingRule()
    {
        var badRule = await CreateRuleAsync(OwnerA, "Bad Rule");
        var goodRule = await CreateRuleAsync(OwnerA, "Good Rule");

        for (var i = 0; i < 4; i++)
        {
            await SeedRuleDispositionAsync(OwnerA, badRule.Id, RecoveryDisposition.Returned, $"bad-{i}");
            await SeedRuleDispositionAsync(OwnerA, goodRule.Id, RecoveryDisposition.Recovered, $"good-{i}");
        }

        var worker = CreateWorker(CircuitBreakerConfig(sampleSize: 4, floor: 0.50));
        await worker.SweepAutoReplayCircuitBreakersAsync(BuildScope(), OwnerA, _recoveryLedger, CancellationToken.None);

        (await _dbContext.AutoReplayRules.SingleAsync(r => r.Id == badRule.Id)).Enabled.Should().BeFalse();
        (await _dbContext.AutoReplayRules.SingleAsync(r => r.Id == goodRule.Id)).Enabled.Should().BeTrue();
    }

    [Fact]
    public async Task SweepAutoReplayCircuitBreakersAsync_AlreadyDisabledRule_NeverReevaluated()
    {
        var rule = await CreateRuleAsync(OwnerA, "Already Off", enabled: false);
        for (var i = 0; i < 4; i++)
        {
            await SeedRuleDispositionAsync(OwnerA, rule.Id, RecoveryDisposition.Returned, $"body-{i}");
        }

        var worker = CreateWorker(CircuitBreakerConfig(sampleSize: 4, floor: 0.50));
        await worker.SweepAutoReplayCircuitBreakersAsync(BuildScope(), OwnerA, _recoveryLedger, CancellationToken.None);

        (await _dbContext.RecoveryEvents.AnyAsync(e => e.EventType == RecoveryEventType.AutoReplayRuleCircuitBreakerTripped))
            .Should().BeFalse("an already-disabled rule has nothing left to trip and must not be re-recorded every sweep");
    }

    [Fact]
    public async Task SweepAutoReplayCircuitBreakersAsync_DifferentOwner_Isolated()
    {
        var ruleB = await CreateRuleAsync(OwnerB, "Owner B Rule");
        for (var i = 0; i < 4; i++)
        {
            await SeedRuleDispositionAsync(OwnerB, ruleB.Id, RecoveryDisposition.Returned, $"body-{i}");
        }

        var worker = CreateWorker(CircuitBreakerConfig(sampleSize: 4, floor: 0.50));
        await worker.SweepAutoReplayCircuitBreakersAsync(BuildScope(), OwnerA, _recoveryLedger, CancellationToken.None);

        (await _dbContext.AutoReplayRules.SingleAsync(r => r.Id == ruleB.Id)).Enabled.Should().BeTrue(
            "sweeping OwnerA must never evaluate, let alone disable, OwnerB's rules");
    }
}
