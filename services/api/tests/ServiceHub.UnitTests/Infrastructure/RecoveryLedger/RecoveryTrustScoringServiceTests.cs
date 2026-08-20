using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

public sealed class RecoveryTrustScoringServiceTests : IDisposable
{
    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";
    private const string SignatureX = "sig-hash-x";
    private const string SignatureY = "sig-hash-y";

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _ledger;
    private readonly RecoveryTrustScoringService _service;

    public RecoveryTrustScoringServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _ledger = new RecoveryLedgerService(_dbContext);
        _service = new RecoveryTrustScoringService(_ledger);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecoveryActor Actor(RecoveryActorKind kind = RecoveryActorKind.User) => new("test-actor", kind);

    private async Task<RecoveryOperation> OpenOperationAsync(
        string ownerId, RecoveryOperationKind kind = RecoveryOperationKind.Replay)
    {
        var result = await _ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = kind,
            Trigger = RecoveryTrigger.Manual,
            Actor = Actor(),
            Reason = kind == RecoveryOperationKind.Purge ? "test purge reason" : null,
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<RecoveryLedgerEntry> BeginEntryAsync(
        RecoveryOperation operation, string? signatureHash, string bodyHash = "body-hash")
    {
        var result = await _ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = operation.OwnerId,
            Actor = Actor(),
            BodyHash = bodyHash,
            SignatureHashSnapshot = signatureHash,
            TargetEntity = "orders-dlq",
        });
        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    /// <summary>Drives one Replay entry to a terminal disposition end-to-end.</summary>
    private async Task<Guid> CreateReplayEntryAsync(
        string ownerId, string? signatureHash, RecoveryDisposition disposition, string bodyHash = "body-hash")
    {
        var operation = await OpenOperationAsync(ownerId, RecoveryOperationKind.Replay);
        var entry = await BeginEntryAsync(operation, signatureHash, bodyHash);

        if (disposition == RecoveryDisposition.Failed)
        {
            var execResult = await _ledger.RecordExecutionAsync(new RecordExecutionRequest
            {
                EntryId = entry.Id,
                OwnerId = ownerId,
                Actor = Actor(),
                Outcome = RecoveryExecutionOutcome.Rejected,
            });
            execResult.IsSuccess.Should().BeTrue();
            return entry.Id;
        }

        var accepted = await _ledger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = ownerId,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });
        accepted.IsSuccess.Should().BeTrue();

        var outcome = disposition switch
        {
            RecoveryDisposition.Recovered => RecoveryObservationOutcome.NoRecurrenceObserved,
            RecoveryDisposition.Returned => RecoveryObservationOutcome.RecurrenceObserved,
            RecoveryDisposition.Unverified => RecoveryObservationOutcome.ObservationUnavailable,
            _ => throw new ArgumentOutOfRangeException(nameof(disposition)),
        };

        var observed = await _ledger.RecordObservationAsync(new RecordObservationRequest
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

    private async Task CreateRejectedPurgeEntryAsync(string ownerId, string signatureHash)
    {
        var operation = await OpenOperationAsync(ownerId, RecoveryOperationKind.Purge);
        var entry = await BeginEntryAsync(operation, signatureHash, "purge-body-hash");

        var result = await _ledger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = ownerId,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Rejected,
        });
        result.IsSuccess.Should().BeTrue();
    }

    // ── 1. No evidence → insufficient trust ────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_NoEvidence_ReportsInsufficientTrust()
    {
        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.IsSuccess.Should().BeTrue();
        var evidence = result.Value;
        evidence.SampleSize.Should().Be(0);
        evidence.VerifiedSuccessRate.Should().BeNull();
        evidence.MeetsL4SampleAndRate.Should().BeFalse();
        evidence.MeetsL5SampleAndRate.Should().BeFalse();
        evidence.Reasons.Should().Contain(r => r.Contains("L0", StringComparison.Ordinal));
    }

    // ── 2. Verified successful recoveries increase metrics correctly ───────

    [Fact]
    public async Task EvaluateAsync_VerifiedRecoveries_IncreasesRateAndSampleSize()
    {
        for (var i = 0; i < 3; i++)
        {
            await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, $"body-{i}");
        }

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.RecoveredCount.Should().Be(3);
        result.Value.SampleSize.Should().Be(3);
        result.Value.VerifiedSuccessRate.Should().Be(1.0);
    }

    // ── 3. Verified Returned outcomes lower the success rate ───────────────

    [Fact]
    public async Task EvaluateAsync_ReturnedOutcomes_LowersVerifiedSuccessRate()
    {
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, "body-1");
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Returned, "body-2");

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.SampleSize.Should().Be(2);
        result.Value.ReturnedCount.Should().Be(1);
        result.Value.VerifiedSuccessRate.Should().Be(0.5);
    }

    // ── 4. Unverified is never counted as verified success ─────────────────

    [Fact]
    public async Task EvaluateAsync_UnverifiedOutcomes_ExcludedFromSampleAndRate()
    {
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, "body-1");
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Unverified, "body-2");
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Unverified, "body-3");

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.UnverifiedCount.Should().Be(2);
        result.Value.SampleSize.Should().Be(1); // only the Recovered entry
        result.Value.VerifiedSuccessRate.Should().Be(1.0); // 1/1, not 1/3
    }

    // ── 5. Declined entries never become successful recoveries ─────────────

    [Fact]
    public async Task EvaluateAsync_DeclinedEntries_NeverCountedAsSuccessOrSample()
    {
        var operation = await OpenOperationAsync(OwnerA, RecoveryOperationKind.Replay);
        var declineRequest = new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("auto-replay", RecoveryActorKind.Automation),
            BodyHash = "declined-body",
            SignatureHashSnapshot = SignatureX,
            TargetEntity = "orders-dlq",
        };
        var declineResult = await _ledger.RecordDeclinedAsync(declineRequest, "RECURRENCE_CAP_EXCEEDED", null);
        declineResult.IsSuccess.Should().BeTrue();

        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, "body-1");

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.DeclinedCount.Should().Be(1);
        result.Value.SampleSize.Should().Be(1); // the Declined entry never enters the sample
        result.Value.VerifiedSuccessRate.Should().Be(1.0);
    }

    // ── 6. Owner isolation ──────────────────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_OwnerIsolation_NeverLeaksAcrossOwners()
    {
        for (var i = 0; i < 20; i++)
        {
            await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, $"a-body-{i}");
        }

        var resultForOwnerB = await _service.EvaluateAsync(OwnerB, SignatureX, RecoveryOperationKind.Replay);

        resultForOwnerB.Value.SampleSize.Should().Be(0);
        resultForOwnerB.Value.RecoveredCount.Should().Be(0);
    }

    // ── 7. Null/empty SignatureHash is rejected, never silently queried ────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task EvaluateAsync_NullOrEmptySignatureHash_FailsValidation(string? signatureHash)
    {
        var result = await _service.EvaluateAsync(OwnerA, signatureHash!, RecoveryOperationKind.Replay);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("TrustScoring.SignatureHashRequired");
    }

    [Fact]
    public async Task EvaluateAsync_NullSignatureHashEntries_NeverAggregatedTogether()
    {
        // Two unrelated failures both happen to have a null SignatureHashSnapshot (e.g. before
        // fingerprinting stabilised). Evaluating a real, named signature must never accidentally
        // pull in entries that share nothing but a null hash.
        var operation = await OpenOperationAsync(OwnerA, RecoveryOperationKind.Replay);
        await BeginEntryAsync(operation, signatureHash: null, "unrelated-body-1");

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.SampleSize.Should().Be(0);
    }

    // ── 8. Distinct signatures never contaminate each other's evidence ─────

    [Fact]
    public async Task EvaluateAsync_DistinctSignatures_EvidenceNeverMixed()
    {
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, "x-body");
        await CreateReplayEntryAsync(OwnerA, SignatureY, RecoveryDisposition.Returned, "y-body");

        var resultX = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);
        var resultY = await _service.EvaluateAsync(OwnerA, SignatureY, RecoveryOperationKind.Replay);

        resultX.Value.VerifiedSuccessRate.Should().Be(1.0);
        resultY.Value.VerifiedSuccessRate.Should().Be(0.0);
    }

    // ── 9 & 10. Exact L4/L5 sample-size and rate thresholds (roadmap §8.7/§8.10) ──

    [Fact]
    public async Task EvaluateAsync_NineSamplesAtPerfectRate_DoesNotMeetL4()
    {
        for (var i = 0; i < 9; i++)
        {
            await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, $"body-{i}");
        }

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.SampleSize.Should().Be(9);
        result.Value.MeetsL4SampleAndRate.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_TenSamplesAtExactlyNinetyFivePercent_MeetsL4()
    {
        // 19/20 = 0.95 exactly, n = 20 (>= 10) — the smallest population that hits the ratio
        // exactly without floating-point rounding ambiguity.
        for (var i = 0; i < 19; i++)
        {
            await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, $"ok-{i}");
        }
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Returned, "bad-1");

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.SampleSize.Should().Be(20);
        result.Value.VerifiedSuccessRate.Should().Be(0.95);
        result.Value.MeetsL4SampleAndRate.Should().BeTrue();
        result.Value.MeetsL5SampleAndRate.Should().BeFalse(); // sample < 30
    }

    [Fact]
    public async Task EvaluateAsync_JustBelowNinetyFivePercent_DoesNotMeetL4()
    {
        // 18/20 = 0.90 < 0.95
        for (var i = 0; i < 18; i++)
        {
            await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, $"ok-{i}");
        }
        for (var i = 0; i < 2; i++)
        {
            await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Returned, $"bad-{i}");
        }

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.MeetsL4SampleAndRate.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_ThirtySamplesAtNinetyNinePercent_MeetsL5()
    {
        // 99/100 = 0.99 exactly, n = 100 (>= 30)
        for (var i = 0; i < 99; i++)
        {
            await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, $"ok-{i}");
        }
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Returned, "bad-1");

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.SampleSize.Should().Be(100);
        result.Value.VerifiedSuccessRate.Should().Be(0.99);
        result.Value.MeetsL5SampleAndRate.Should().BeTrue();
    }

    [Fact]
    public async Task EvaluateAsync_TwentyNineSamplesAtPerfectRate_DoesNotMeetL5()
    {
        for (var i = 0; i < 29; i++)
        {
            await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, $"body-{i}");
        }

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.SampleSize.Should().Be(29);
        result.Value.VerifiedSuccessRate.Should().Be(1.0);
        result.Value.MeetsL4SampleAndRate.Should().BeTrue();
        result.Value.MeetsL5SampleAndRate.Should().BeFalse();
    }

    // ── 11. Deterministic / reproducible ────────────────────────────────────

    [Fact]
    public async Task EvaluateAsync_CalledTwice_ReturnsIdenticalResult()
    {
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, "body-1");
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Returned, "body-2");

        var first = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);
        var second = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        first.Value.Should().BeEquivalentTo(second.Value);
    }

    // ── 13. unsafe/duplicate evidence — fleet-level vs per-signature (§8.10) ──

    [Fact]
    public async Task EvaluateAsync_NoOutcomeFlags_ReportsBothDisqualifiersAbsent()
    {
        for (var i = 0; i < 50; i++)
        {
            await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, $"body-{i}");
        }

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.UnsafeOutcomePresent.Should().BeFalse();
        result.Value.DuplicateAssociationPresent.Should().BeFalse();
    }

    [Fact]
    public async Task EvaluateAsync_UnsafeFlagOnAnyEntry_DisqualifiesEveryOwnerSignature_FleetLevel()
    {
        var flaggedEntryId = await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered);
        await CreateReplayEntryAsync(OwnerA, SignatureY, RecoveryDisposition.Recovered);

        var flagResult = await _ledger.RecordOutcomeFlagAsync(
            flaggedEntryId, OwnerA, Actor(), RecoveryOutcomeFlagKind.Unsafe, "customer reported data loss");
        flagResult.IsSuccess.Should().BeTrue();

        var evidenceForFlaggedSignature = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);
        var evidenceForOtherSignature = await _service.EvaluateAsync(OwnerA, SignatureY, RecoveryOperationKind.Replay);

        evidenceForFlaggedSignature.Value.UnsafeOutcomePresent.Should().BeTrue();
        evidenceForOtherSignature.Value.UnsafeOutcomePresent.Should().BeTrue(
            "an unsafe outcome disqualifies the owner's whole fleet, not only the flagged signature (§8.10)");
    }

    [Fact]
    public async Task EvaluateAsync_DuplicateFlagOnOneSignature_DoesNotDisqualifyAnotherSignature_PerSignature()
    {
        var flaggedEntryId = await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered);
        await CreateReplayEntryAsync(OwnerA, SignatureY, RecoveryDisposition.Recovered);

        var flagResult = await _ledger.RecordOutcomeFlagAsync(
            flaggedEntryId, OwnerA, Actor(), RecoveryOutcomeFlagKind.DuplicateBusinessEffect, "double-charged customer");
        flagResult.IsSuccess.Should().BeTrue();

        var evidenceForFlaggedSignature = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);
        var evidenceForOtherSignature = await _service.EvaluateAsync(OwnerA, SignatureY, RecoveryOperationKind.Replay);

        evidenceForFlaggedSignature.Value.DuplicateAssociationPresent.Should().BeTrue();
        evidenceForOtherSignature.Value.DuplicateAssociationPresent.Should().BeFalse(
            "duplicate_association is a per-signature disqualifier, never bleeding into an unrelated signature (§8.10)");
        evidenceForFlaggedSignature.Value.UnsafeOutcomePresent.Should().BeFalse(
            "a DuplicateBusinessEffect flag must never be mistaken for an Unsafe flag");
    }

    [Fact]
    public async Task EvaluateAsync_UnsafeFlagUnderOneOwner_DoesNotLeakToAnotherOwner()
    {
        var flaggedEntryId = await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered);
        await CreateReplayEntryAsync(OwnerB, SignatureX, RecoveryDisposition.Recovered);

        var flagResult = await _ledger.RecordOutcomeFlagAsync(
            flaggedEntryId, OwnerA, Actor(), RecoveryOutcomeFlagKind.Unsafe, "owner A incident");
        flagResult.IsSuccess.Should().BeTrue();

        var evidenceOwnerB = await _service.EvaluateAsync(OwnerB, SignatureX, RecoveryOperationKind.Replay);

        evidenceOwnerB.Value.UnsafeOutcomePresent.Should().BeFalse("owner isolation must hold for fleet-level flags too");
    }

    // ── 14. AWS/GCP-style unverifiable evidence is represented honestly ────

    [Fact]
    public async Task EvaluateAsync_AllUnverifiedEvidence_NeverFabricatesSuccess()
    {
        // Simulates a non-verification-capable provider (AWS/GCP default, §14): every replay
        // closes as Unverified because coverage can never prove absence.
        for (var i = 0; i < 15; i++)
        {
            await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Unverified, $"body-{i}");
        }

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.UnverifiedCount.Should().Be(15);
        result.Value.SampleSize.Should().Be(0);
        result.Value.VerifiedSuccessRate.Should().BeNull();
        result.Value.MeetsL4SampleAndRate.Should().BeFalse();
    }

    // ── 15. No trust calculation grants autonomy or writes anything ────────

    [Fact]
    public async Task EvaluateAsync_NeverWritesToTheLedger()
    {
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, "body-1");

        var entryCountBefore = await _dbContext.RecoveryLedgerEntries.CountAsync();
        var eventCountBefore = await _dbContext.RecoveryEvents.CountAsync();

        await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);
        await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        var entryCountAfter = await _dbContext.RecoveryLedgerEntries.CountAsync();
        var eventCountAfter = await _dbContext.RecoveryEvents.CountAsync();

        entryCountAfter.Should().Be(entryCountBefore);
        eventCountAfter.Should().Be(eventCountBefore);
    }

    // ── Purge attempts never pollute a Replay trust score ───────────────────

    [Fact]
    public async Task EvaluateAsync_RejectedPurgeSharingSignature_NeverCountedInReplayScore()
    {
        await CreateReplayEntryAsync(OwnerA, SignatureX, RecoveryDisposition.Recovered, "replay-body");
        await CreateRejectedPurgeEntryAsync(OwnerA, SignatureX); // also produces Disposition.Failed

        var result = await _service.EvaluateAsync(OwnerA, SignatureX, RecoveryOperationKind.Replay);

        result.Value.SampleSize.Should().Be(1); // the purge's Failed entry must not be counted
        result.Value.FailedCount.Should().Be(0);
        result.Value.VerifiedSuccessRate.Should().Be(1.0);
    }
}
