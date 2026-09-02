using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

/// <summary>
/// Tests for <see cref="RecoveryRehearsalService"/> (roadmap §7 W1.2) — the gate's verdict for a
/// real, already-recorded entry's identity, with no mutation and no dependency capable of
/// reaching a broker.
/// </summary>
public sealed class RecoveryRehearsalServiceTests : IDisposable
{
    private const string OwnerId = "entra:test-owner-123";
    private static readonly Guid NamespaceId = Guid.NewGuid();

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _ledger;
    private readonly RecoveryEligibilityGate _gate;
    private readonly RecoveryRehearsalService _rehearsal;

    public RecoveryRehearsalServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _ledger = new RecoveryLedgerService(_dbContext);
        _gate = new RecoveryEligibilityGate(_ledger, NullLogger<RecoveryEligibilityGate>.Instance);
        _rehearsal = new RecoveryRehearsalService(_ledger, _gate);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    public static IEnumerable<object[]> NullConstructorArgs => new[]
    {
        new object[] { true, false },
        new object[] { false, true },
    };

    [Theory]
    [MemberData(nameof(NullConstructorArgs))]
    public void Constructor_NullDependency_Throws(bool nullLedger, bool nullGate)
    {
        var act = () => new RecoveryRehearsalService(
            nullLedger ? null! : _ledger,
            nullGate ? null! : _gate);

        act.Should().Throw<ArgumentNullException>();
    }

    private async Task<Guid> SeedEntryAsync(
        string ownerId = OwnerId,
        RecoveryOperationKind actionKind = RecoveryOperationKind.Replay,
        string entityName = "orders",
        string bodyHash = "hash-1",
        string? signatureHash = null,
        CloudProviderType? provider = CloudProviderType.Azure,
        EnvironmentType? environment = EnvironmentType.Dev)
    {
        var opened = await _ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = actionKind,
            Trigger = RecoveryTrigger.Manual,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            NamespaceId = NamespaceId,
            ScopeDescription = "test",
            TargetCount = 1,
        });

        var entry = await _ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = opened.Value.Id,
            OwnerId = ownerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            NamespaceId = NamespaceId,
            ProviderSnapshot = provider,
            EnvironmentSnapshot = environment,
            EntityNameSnapshot = entityName,
            BodyHash = bodyHash,
            SignatureHashSnapshot = signatureHash,
            TargetEntity = entityName,
        });

        return entry.Value.Id;
    }

    [Fact]
    public async Task RehearseAsync_UnknownEntry_ReturnsNotFound()
    {
        var result = await _rehearsal.RehearseAsync(Guid.NewGuid(), OwnerId, RecoveryActorKind.Automation);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RecoveryLedger.EntryNotFound");
    }

    [Fact]
    public async Task RehearseAsync_EntryBelongsToDifferentOwner_ReturnsNotFound()
    {
        var entryId = await SeedEntryAsync(ownerId: OwnerId);

        var result = await _rehearsal.RehearseAsync(entryId, "entra:someone-else", RecoveryActorKind.Automation);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RecoveryLedger.EntryNotFound");
    }

    [Fact]
    public async Task RehearseAsync_ProductionEnvironment_DeniesJustLikeARealAttemptWould()
    {
        var entryId = await SeedEntryAsync(environment: EnvironmentType.Prod);

        var result = await _rehearsal.RehearseAsync(entryId, OwnerId, RecoveryActorKind.User);

        result.IsSuccess.Should().BeTrue();
        result.Value.Decision.Verdict.Should().Be(EligibilityVerdict.Deny);
        result.Value.Decision.ReasonCode.Should().Be("PRODUCTION_ELEVATION_REQUIRED");
    }

    [Fact]
    public async Task RehearseAsync_AutomationActorNoGrant_EscalatesWithAutonomyReason()
    {
        var entryId = await SeedEntryAsync(signatureHash: "sig-1");

        var result = await _rehearsal.RehearseAsync(entryId, OwnerId, RecoveryActorKind.Automation);

        result.IsSuccess.Should().BeTrue();
        result.Value.Decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        result.Value.Decision.ReasonCode.Should().Be("AUTONOMY_GRANT_INSUFFICIENT");
        result.Value.ActorKindEvaluated.Should().Be(RecoveryActorKind.Automation);
        result.Value.EntryId.Should().Be(entryId);
    }

    [Fact]
    public async Task RehearseAsync_AutomationActorWithStandingGrant_AllowsExercisingTheAcceptPath()
    {
        var entryId = await SeedEntryAsync(signatureHash: "sig-standing");
        await _ledger.RecordAutonomyGrantTransitionAsync(
            OwnerId, "sig-standing", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "test", evidenceJson: null);

        var result = await _rehearsal.RehearseAsync(entryId, OwnerId, RecoveryActorKind.Automation);

        result.IsSuccess.Should().BeTrue();
        result.Value.Decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task RehearseAsync_SameEntryDifferentActorKind_CanChangeTheVerdict()
    {
        // An entry a human originally attempted, with no autonomy grant for its signature — as
        // User it's allowed; rehearsed as Automation it escalates. This is the whole point of
        // letting the caller choose actorKind independently of the entry's own history.
        var entryId = await SeedEntryAsync(signatureHash: "sig-mixed");

        var asUser = await _rehearsal.RehearseAsync(entryId, OwnerId, RecoveryActorKind.User);
        var asAutomation = await _rehearsal.RehearseAsync(entryId, OwnerId, RecoveryActorKind.Automation);

        asUser.Value.Decision.Verdict.Should().Be(EligibilityVerdict.Allow);
        asAutomation.Value.Decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
    }

    [Fact]
    public async Task RehearseAsync_NeverWritesToTheLedger()
    {
        var entryId = await SeedEntryAsync(signatureHash: "sig-1");
        var eventsBefore = await _ledger.GetEventsForOperationAsync(
            (await _ledger.GetEntryAsync(entryId, OwnerId))!.OperationId, OwnerId);

        await _rehearsal.RehearseAsync(entryId, OwnerId, RecoveryActorKind.Automation);
        await _rehearsal.RehearseAsync(entryId, OwnerId, RecoveryActorKind.User);

        var eventsAfter = await _ledger.GetEventsForOperationAsync(
            (await _ledger.GetEntryAsync(entryId, OwnerId))!.OperationId, OwnerId);

        eventsAfter.Count.Should().Be(eventsBefore.Count);
    }
}
