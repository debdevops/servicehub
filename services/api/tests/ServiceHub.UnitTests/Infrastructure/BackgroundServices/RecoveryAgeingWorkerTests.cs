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

namespace ServiceHub.UnitTests.Infrastructure.BackgroundServices;

/// <summary>
/// Phase 5 (Ageing) coverage for <see cref="RecoveryAgeingWorker"/>: the two-pass flag-then-expire
/// sweep, and the invariant it exists to enforce — <see cref="RecoveryEntryState.Expired"/> is
/// reachable only through a transition whose immediately preceding event is
/// <see cref="RecoveryEventType.AgeingFlagged"/> (roadmap §7.2).
/// </summary>
public sealed class RecoveryAgeingWorkerTests : IDisposable
{
    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _recoveryLedger;

    public RecoveryAgeingWorkerTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _recoveryLedger = new RecoveryLedgerService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecoveryAgeingWorker CreateWorker(int thresholdDays = 1) =>
        new(
            Mock.Of<IServiceProvider>(),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["RecoveryEvidence:AgeingThresholdDays"] = thresholdDays.ToString(),
            }).Build(),
            NullLogger<RecoveryAgeingWorker>.Instance);

    private static IServiceProvider BuildScope(IRecoveryLedger recoveryLedger)
    {
        var services = new ServiceCollection();
        services.AddSingleton(recoveryLedger);
        return services.BuildServiceProvider();
    }

    private async Task<RecoveryLedgerEntry> BeginEntryAsync(string ownerId, DateTimeOffset begunAt)
    {
        var operation = await _recoveryLedger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });

        var begin = await _recoveryLedger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Value.Id,
            OwnerId = ownerId,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            EntityNameSnapshot = "orders-dlq",
            BodyHash = "hash",
            TargetEntity = "orders-dlq",
        });

        // BegunAt is immutable (set once, at BeginEntryAsync) and is not in the ledger's mutable
        // projection, so it cannot be changed through IRecoveryLedger or EF SaveChanges — a raw
        // SQL statement is the only way to backdate it for a test, exactly mirroring how an
        // entry would look after genuinely sitting open for days.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE RecoveryLedgerEntries SET BegunAt = {begunAt:O} WHERE Id = {begin.Value.Id}");

        return begin.Value;
    }

    [Fact]
    public async Task SweepOwnerAsync_EntryPastThreshold_FirstSweepOnlyFlags_StateUnchanged()
    {
        var entry = await BeginEntryAsync(OwnerA, DateTimeOffset.UtcNow.AddDays(-10));
        var worker = CreateWorker(thresholdDays: 7);

        await worker.SweepOwnerAsync(BuildScope(_recoveryLedger), OwnerA, CancellationToken.None);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Executing);
        (await _recoveryLedger.HasAgeingFlagAsync(entry.Id, OwnerA)).Should().BeTrue();
    }

    [Fact]
    public async Task SweepOwnerAsync_AlreadyFlaggedEntryStillPastThreshold_SecondSweepExpiresIt()
    {
        var entry = await BeginEntryAsync(OwnerA, DateTimeOffset.UtcNow.AddDays(-10));
        var worker = CreateWorker(thresholdDays: 7);
        var scope = BuildScope(_recoveryLedger);

        await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None); // flags
        await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None); // expires

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Expired);
        updated.Disposition.Should().Be(RecoveryDisposition.Expired);
    }

    [Fact]
    public async Task SweepOwnerAsync_ExpiryIsOnlyEverPrecededByAgeingFlaggedEvent()
    {
        var entry = await BeginEntryAsync(OwnerA, DateTimeOffset.UtcNow.AddDays(-10));
        var worker = CreateWorker(thresholdDays: 7);
        var scope = BuildScope(_recoveryLedger);

        await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None);
        await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None);

        var events = await _dbContext.RecoveryEvents
            .Where(e => e.EntryId == entry.Id)
            .OrderBy(e => e.Seq)
            .ToListAsync();

        var expiryIndex = events.FindIndex(e => e.EventType == RecoveryEventType.DispositionSet);
        expiryIndex.Should().BeGreaterThan(0);
        events[expiryIndex - 1].EventType.Should().Be(RecoveryEventType.AgeingFlagged);
    }

    [Fact]
    public async Task SweepOwnerAsync_EntryNotYetPastThreshold_LeftUntouched()
    {
        var entry = await BeginEntryAsync(OwnerA, DateTimeOffset.UtcNow.AddDays(-1));
        var worker = CreateWorker(thresholdDays: 7);

        await worker.SweepOwnerAsync(BuildScope(_recoveryLedger), OwnerA, CancellationToken.None);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Executing);
        (await _recoveryLedger.HasAgeingFlagAsync(entry.Id, OwnerA)).Should().BeFalse();
    }

    [Fact]
    public async Task SweepOwnerAsync_TerminalEntry_NeverFlaggedOrExpired()
    {
        var entry = await BeginEntryAsync(OwnerA, DateTimeOffset.UtcNow.AddDays(-10));
        await _recoveryLedger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
            Outcome = RecoveryExecutionOutcome.Rejected,
        });

        var worker = CreateWorker(thresholdDays: 7);
        var scope = BuildScope(_recoveryLedger);
        await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None);
        await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.ExecutionFailed); // untouched — already terminal
        (await _recoveryLedger.HasAgeingFlagAsync(entry.Id, OwnerA)).Should().BeFalse();
    }

    [Fact]
    public async Task SweepOwnerAsync_RepeatedSweeps_AreIdempotent_NoDuplicateFlagOrExpiryEvents()
    {
        var entry = await BeginEntryAsync(OwnerA, DateTimeOffset.UtcNow.AddDays(-10));
        var worker = CreateWorker(thresholdDays: 7);
        var scope = BuildScope(_recoveryLedger);

        // Simulates a restart-then-resweep cycle plus an extra redundant cycle: flag, expire,
        // then two more sweeps that must both be safe no-ops on an already-terminal entry.
        for (var i = 0; i < 4; i++)
        {
            await worker.SweepOwnerAsync(scope, OwnerA, CancellationToken.None);
        }

        var flagEvents = await _dbContext.RecoveryEvents
            .Where(e => e.EntryId == entry.Id && e.EventType == RecoveryEventType.AgeingFlagged)
            .CountAsync();
        var expiryEvents = await _dbContext.RecoveryEvents
            .Where(e => e.EntryId == entry.Id && e.EventType == RecoveryEventType.DispositionSet)
            .CountAsync();

        flagEvents.Should().Be(1);
        expiryEvents.Should().Be(1);
    }

    [Fact]
    public async Task SweepOwnerAsync_SimulatedRestart_FreshScopeStillCompletesFlagThenExpire()
    {
        var entry = await BeginEntryAsync(OwnerA, DateTimeOffset.UtcNow.AddDays(-10));
        var worker = CreateWorker(thresholdDays: 7);

        // Each sweep call below builds an entirely new scope against the same durable DB —
        // nothing is cached in the worker between calls, so this is indistinguishable from two
        // sweeps run by two different process instances (a restart).
        await worker.SweepOwnerAsync(BuildScope(_recoveryLedger), OwnerA, CancellationToken.None);
        await worker.SweepOwnerAsync(BuildScope(_recoveryLedger), OwnerA, CancellationToken.None);

        var updated = await _dbContext.RecoveryLedgerEntries.AsNoTracking().SingleAsync(e => e.Id == entry.Id);
        updated.State.Should().Be(RecoveryEntryState.Expired);
    }

    [Fact]
    public async Task SweepOwnerAsync_OtherOwnersDueEntry_NotTouched()
    {
        var entryA = await BeginEntryAsync(OwnerA, DateTimeOffset.UtcNow.AddDays(-10));
        var entryB = await BeginEntryAsync(OwnerB, DateTimeOffset.UtcNow.AddDays(-10));

        var worker = CreateWorker(thresholdDays: 7);
        await worker.SweepOwnerAsync(BuildScope(_recoveryLedger), OwnerA, CancellationToken.None);

        (await _recoveryLedger.HasAgeingFlagAsync(entryA.Id, OwnerA)).Should().BeTrue();
        (await _recoveryLedger.HasAgeingFlagAsync(entryB.Id, OwnerB)).Should().BeFalse();
    }

    [Fact]
    public async Task SweepOwnerAsync_ConcurrentSweeps_ProduceOnlyOneFlagEvent()
    {
        var entry = await BeginEntryAsync(OwnerA, DateTimeOffset.UtcNow.AddDays(-10));
        var worker = CreateWorker(thresholdDays: 7);

        // Each concurrent sweep needs its own DbContext instance — sharing one across the two
        // Task.WhenAll branches would violate EF Core's single-threaded-per-instance contract and
        // fail with a ConcurrencyDetector error unrelated to the invariant under test. The second
        // context shares the first's open connection so both see the same in-memory database,
        // mirroring two independent DI scopes (as production's IServiceScopeFactory would hand
        // out) racing against the same owner's static lock (RecoveryLedgerService.OwnerLocks).
        using var otherContext = new DlqDbContext(
            new DbContextOptionsBuilder<DlqDbContext>()
                .UseSqlite(_dbContext.Database.GetDbConnection())
                .Options);
        var otherLedger = new RecoveryLedgerService(otherContext);

        await Task.WhenAll(
            worker.SweepOwnerAsync(BuildScope(_recoveryLedger), OwnerA, CancellationToken.None),
            worker.SweepOwnerAsync(BuildScope(otherLedger), OwnerA, CancellationToken.None));

        var flagEvents = await _dbContext.RecoveryEvents
            .Where(e => e.EntryId == entry.Id && e.EventType == RecoveryEventType.AgeingFlagged)
            .CountAsync();
        flagEvents.Should().Be(1);
    }

    [Fact]
    public async Task SweepOwnerAsync_CancelledBeforeDueEntry_ThrowsOperationCanceledException()
    {
        await BeginEntryAsync(OwnerA, DateTimeOffset.UtcNow.AddDays(-10));
        var worker = CreateWorker(thresholdDays: 7);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await worker.SweepOwnerAsync(BuildScope(_recoveryLedger), OwnerA, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
