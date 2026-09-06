using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

/// <summary>
/// Proves every <see cref="OutcomeMetricsOverview"/> figure traces to a real ledger row — the
/// roadmap next-chapter M4.1 requirement that nothing here is modelled, estimated, or
/// extrapolated. Each test drives the real <see cref="RecoveryLedgerService"/> through the exact
/// state transitions an operator or the verification worker would, then asserts the service reads
/// back what was actually written.
/// </summary>
public sealed class OutcomeMetricsServiceTests : IDisposable
{
    private const string OwnerA = "outcome-owner-a";
    private const string OwnerB = "outcome-owner-b";

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _ledger;
    private readonly OutcomeMetricsService _sut;

    public OutcomeMetricsServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _ledger = new RecoveryLedgerService(_dbContext);
        _sut = new OutcomeMetricsService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecoveryActor UserActor() => new("operator", RecoveryActorKind.User);
    private static RecoveryActor AutomationActor() => new("auto-replay-rule", RecoveryActorKind.Automation);

    private async Task<RecoveryLedgerEntry> RecoverAsync(string ownerId, RecoveryActor actor)
    {
        var operation = await _ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = actor.Kind == RecoveryActorKind.Automation ? RecoveryTrigger.AutoRule : RecoveryTrigger.Manual,
            Actor = actor,
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });
        operation.IsSuccess.Should().BeTrue();

        var entry = await _ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Value.Id,
            OwnerId = ownerId,
            Actor = actor,
            BodyHash = $"body-{Guid.NewGuid():N}",
            TargetEntity = "orders-dlq",
        });
        entry.IsSuccess.Should().BeTrue();

        var execution = await _ledger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Value.Id,
            OwnerId = ownerId,
            Actor = actor,
            Outcome = RecoveryExecutionOutcome.Accepted,
        });
        execution.IsSuccess.Should().BeTrue();

        var observation = await _ledger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Value.Id,
            OwnerId = ownerId,
            Actor = actor,
            Outcome = RecoveryObservationOutcome.NoRecurrenceObserved,
        });
        observation.IsSuccess.Should().BeTrue();
        return observation.Value;
    }

    private async Task<RecoveryLedgerEntry> WriteOffAsync(string ownerId)
    {
        var operation = await _ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            Actor = UserActor(),
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });

        var entry = await _ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Value.Id,
            OwnerId = ownerId,
            Actor = UserActor(),
            BodyHash = $"body-{Guid.NewGuid():N}",
            TargetEntity = "orders-dlq",
        });

        var disposed = await _ledger.SetDispositionAsync(
            entry.Value.Id, ownerId, UserActor(), "unrecoverable", CancellationToken.None);
        disposed.IsSuccess.Should().BeTrue();
        return disposed.Value;
    }

    private async Task DeclineAsync(string ownerId)
    {
        var operation = await _ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.AutoRule,
            Actor = AutomationActor(),
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });

        var declined = await _ledger.RecordDeclinedAsync(
            new BeginRecoveryEntryRequest
            {
                OperationId = operation.Value.Id,
                OwnerId = ownerId,
                Actor = AutomationActor(),
                BodyHash = $"body-{Guid.NewGuid():N}",
                TargetEntity = "orders-dlq",
            },
            reasonCode: "recurrence-lineage-cap",
            detailJson: null,
            CancellationToken.None);
        declined.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Constructor_NullDbContext_Throws()
    {
        var act = () => new OutcomeMetricsService(null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetOverviewAsync_EmptyOwnerId_Throws()
    {
        var act = () => _sut.GetOverviewAsync(" ");
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("ownerId");
    }

    [Fact]
    public async Task GetOverviewAsync_NoDataForOwner_ReturnsZeroedSnapshot()
    {
        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.MessagesRecovered.Should().Be(0);
        overview.MessagesAbandoned.Should().Be(0);
        overview.MedianSecondsToVerifiedRecovery.Should().BeNull();
        overview.AutonomousRecoveries.Should().Be(0);
        overview.GateRefusals.Should().Be(0);
    }

    [Fact]
    public async Task GetOverviewAsync_CountsRecoveredEntries()
    {
        await RecoverAsync(OwnerA, UserActor());
        await RecoverAsync(OwnerA, UserActor());

        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.MessagesRecovered.Should().Be(2);
        overview.MedianSecondsToVerifiedRecovery.Should().NotBeNull();
        overview.MedianSecondsToVerifiedRecovery!.Value.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task GetOverviewAsync_CountsWrittenOffEntriesAsAbandoned()
    {
        await WriteOffAsync(OwnerA);

        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.MessagesAbandoned.Should().Be(1);
        overview.MessagesRecovered.Should().Be(0);
    }

    [Fact]
    public async Task GetOverviewAsync_CountsAutomationRecoveriesAsAutonomous()
    {
        await RecoverAsync(OwnerA, AutomationActor());
        await RecoverAsync(OwnerA, UserActor());

        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.MessagesRecovered.Should().Be(2);
        overview.AutonomousRecoveries.Should().Be(1);
    }

    [Fact]
    public async Task GetOverviewAsync_CountsEligibilityDeclinedEventsAsGateRefusals()
    {
        await DeclineAsync(OwnerA);
        await DeclineAsync(OwnerA);

        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.GateRefusals.Should().Be(2);
    }

    [Fact]
    public async Task GetOverviewAsync_ScopesToCallerOwner()
    {
        await RecoverAsync(OwnerA, UserActor());
        await RecoverAsync(OwnerB, UserActor());
        await WriteOffAsync(OwnerB);
        await DeclineAsync(OwnerB);

        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.MessagesRecovered.Should().Be(1);
        overview.MessagesAbandoned.Should().Be(0);
        overview.GateRefusals.Should().Be(0);
    }

    [Fact]
    public async Task GetOverviewAsync_ExcludesEntriesOutsideTheWindow()
    {
        var entry = await RecoverAsync(OwnerA, UserActor());

        // Simulate an old recovery by moving ClosedAt outside the default 7-day window — a raw
        // update, since RecoveryLedgerEntry's mutable projection is only ever written by
        // IRecoveryLedger alongside a justifying event, never by a test constructing the row.
        var tracked = await _dbContext.RecoveryLedgerEntries.FindAsync(entry.Id);
        tracked!.ClosedAt = DateTimeOffset.UtcNow.AddDays(-30);
        await _dbContext.SaveChangesAsync();

        var overview = await _sut.GetOverviewAsync(OwnerA, TimeSpan.FromDays(7));

        overview.MessagesRecovered.Should().Be(0);
    }
}
