using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Shared.Results;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

public sealed class RecoveryLedgerServiceTests : IDisposable
{
    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _service;

    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";

    public RecoveryLedgerServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _service = new RecoveryLedgerService(_dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private static RecoveryActor Actor(string identity = "test-actor") => new(identity, RecoveryActorKind.User);

    private async Task<RecoveryOperation> OpenOperationAsync(
        string ownerId = OwnerA, RecoveryOperationKind kind = RecoveryOperationKind.Replay, string? reason = null)
    {
        var result = await _service.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = kind,
            Trigger = RecoveryTrigger.Manual,
            Actor = Actor(),
            Reason = reason ?? (kind == RecoveryOperationKind.Purge ? "test purge reason" : null),
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<RecoveryLedgerEntry> BeginEntryAsync(RecoveryOperation operation)
    {
        var result = await _service.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = operation.OwnerId,
            Actor = Actor(),
            BodyHash = "body-hash-1",
            TargetEntity = "orders-dlq",
        });

        result.IsSuccess.Should().BeTrue();
        return result.Value;
    }

    private async Task<(RecoveryOperation Operation, RecoveryLedgerEntry Entry)> OpenAndBeginAsync(
        string ownerId = OwnerA, RecoveryOperationKind kind = RecoveryOperationKind.Replay)
    {
        var operation = await OpenOperationAsync(ownerId, kind);
        var entry = await BeginEntryAsync(operation);
        return (operation, entry);
    }

    // ── OpenOperationAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task OpenOperationAsync_Purge_WithoutReason_Fails()
    {
        var result = await _service.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = OwnerA,
            Kind = RecoveryOperationKind.Purge,
            Trigger = RecoveryTrigger.Manual,
            Actor = Actor(),
            Reason = null,
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task OpenOperationAsync_ValidRequest_AppendsOperationOpenedEvent()
    {
        var operation = await OpenOperationAsync();

        var events = await _dbContext.RecoveryEvents.Where(e => e.OperationId == operation.Id).ToListAsync();
        events.Should().ContainSingle(e => e.EventType == RecoveryEventType.OperationOpened);
        events[0].Seq.Should().Be(1);
        events[0].PrevHash.Should().Be(RecoveryHashChain.GenesisHash);
    }

    // ── Full lifecycle happy paths ──────────────────────────────────────────

    [Fact]
    public async Task Lifecycle_ReplayAcceptedThenNoRecurrence_ReachesRecovered()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var executed = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });
        executed.IsSuccess.Should().BeTrue();
        executed.Value.State.Should().Be(RecoveryEntryState.Observing);
        executed.Value.ObservationWindowEndsAt.Should().NotBeNull();

        var observed = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.NoRecurrenceObserved,
        });

        observed.IsSuccess.Should().BeTrue();
        observed.Value.State.Should().Be(RecoveryEntryState.Recovered);
        observed.Value.Disposition.Should().Be(RecoveryDisposition.Recovered);
        observed.Value.VerificationResult.Should().Be(VerificationResult.Recovered);
        observed.Value.ClosedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Lifecycle_ReplayAcceptedThenRecurrence_ReachesReturned()
    {
        var (_, entry) = await OpenAndBeginAsync();

        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        var observed = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.RecurrenceObserved,
            Confidence = VerificationConfidence.Exact,
        });

        observed.IsSuccess.Should().BeTrue();
        observed.Value.State.Should().Be(RecoveryEntryState.Returned);
        observed.Value.Disposition.Should().Be(RecoveryDisposition.Returned);
        observed.Value.VerificationConfidence.Should().Be(VerificationConfidence.Exact);
    }

    [Fact]
    public async Task Lifecycle_PurgeAccepted_ReachesDiscarded()
    {
        var (_, entry) = await OpenAndBeginAsync(kind: RecoveryOperationKind.Purge);

        var executed = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        executed.IsSuccess.Should().BeTrue();
        executed.Value.State.Should().Be(RecoveryEntryState.Discarded);
        executed.Value.Disposition.Should().Be(RecoveryDisposition.Discarded);
        executed.Value.ClosedAt.Should().NotBeNull();
        executed.Value.ObservationWindowEndsAt.Should().BeNull();
    }

    [Fact]
    public async Task Lifecycle_ExecutionRejected_ReachesExecutionFailed()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var executed = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Rejected,
        });

        executed.IsSuccess.Should().BeTrue();
        executed.Value.State.Should().Be(RecoveryEntryState.ExecutionFailed);
        executed.Value.Disposition.Should().Be(RecoveryDisposition.Failed);
    }

    [Fact]
    public async Task Lifecycle_ExecutionUnknownThenWrittenOff_ReachesWrittenOff()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var executed = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Unknown,
        });
        executed.Value.State.Should().Be(RecoveryEntryState.ExecutionUnknown);

        var disposed = await _service.SetDispositionAsync(entry.Id, OwnerA, Actor(), "operator gave up");

        disposed.IsSuccess.Should().BeTrue();
        disposed.Value.State.Should().Be(RecoveryEntryState.WrittenOff);
        disposed.Value.Disposition.Should().Be(RecoveryDisposition.WrittenOff);
    }

    // ── Illegal transitions ─────────────────────────────────────────────────

    [Fact]
    public async Task RecordObservationAsync_BeforeExecuted_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var result = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.NoRecurrenceObserved,
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task RecordExecutionAsync_AlreadyTerminal_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync(kind: RecoveryOperationKind.Purge);

        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        var second = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        second.IsFailure.Should().BeTrue();
        second.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task SetDispositionAsync_AlreadyTerminal_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync(kind: RecoveryOperationKind.Purge);

        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        var result = await _service.SetDispositionAsync(entry.Id, OwnerA, Actor(), "too late");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Conflict);
    }

    [Fact]
    public async Task SetDispositionAsync_EmptyReason_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync();

        var result = await _service.SetDispositionAsync(entry.Id, OwnerA, Actor(), "   ");

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    [Fact]
    public async Task RecordObservationAsync_RecurrenceObservedWithoutConfidence_Fails()
    {
        var (_, entry) = await OpenAndBeginAsync();
        await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        var result = await _service.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryObservationOutcome.RecurrenceObserved,
            Confidence = null,
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.Validation);
    }

    // ── Owner isolation ──────────────────────────────────────────────────────

    [Fact]
    public async Task BeginEntryAsync_OperationBelongsToDifferentOwner_ReturnsNotFound()
    {
        var operation = await OpenOperationAsync(OwnerA);

        var result = await _service.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Id,
            OwnerId = OwnerB,
            Actor = Actor(),
            BodyHash = "hash",
            TargetEntity = "orders-dlq",
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task RecordExecutionAsync_EntryBelongsToDifferentOwner_ReturnsNotFound()
    {
        var (_, entry) = await OpenAndBeginAsync(OwnerA);

        var result = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerB,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    [Fact]
    public async Task GetOperationAsync_WrongOwner_ReturnsNull()
    {
        var operation = await OpenOperationAsync(OwnerA);

        var result = await _service.GetOperationAsync(operation.Id, OwnerB);

        result.Should().BeNull();
    }

    [Fact]
    public async Task QueryEntriesAsync_OnlyReturnsRequestedOwnersEntries()
    {
        var (_, entryA) = await OpenAndBeginAsync(OwnerA);
        var (_, entryB) = await OpenAndBeginAsync(OwnerB);

        var resultA = await _service.QueryEntriesAsync(new RecoveryEntryQuery { OwnerId = OwnerA });

        resultA.Should().ContainSingle(e => e.Id == entryA.Id);
        resultA.Should().NotContain(e => e.Id == entryB.Id);
    }

    // ── Bookkeeping ──────────────────────────────────────────────────────────

    [Fact]
    public async Task LastEventSeq_AdvancesWithEachAppendedEvent()
    {
        var (_, entry) = await OpenAndBeginAsync();
        var seqAfterBegin = entry.LastEventSeq;
        seqAfterBegin.Should().BeGreaterThan(0);

        var afterExecution = await _service.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id,
            OwnerId = OwnerA,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });

        afterExecution.Value.LastEventSeq.Should().BeGreaterThan(seqAfterBegin);
    }

    // ── Concurrency: two owners' Seq sequences advance independently ────────
    //
    // A single DbContext instance cannot safely run concurrent operations (EF Core throws if a
    // second operation starts before the first completes), so genuine concurrent writers need
    // separate DbContext/connection instances — exactly how ASP.NET Core's per-request Scoped
    // DlqDbContext behaves in production. This test uses a temp-file SQLite database (rather than
    // the class fixture's :memory: connection) so multiple real connections can write to it at
    // once, serialized by SQLite itself plus RecoveryLedgerService's static per-owner semaphore.

    [Fact]
    public async Task ConcurrentAppends_TwoOwners_SequencesAdvanceIndependentlyAndContiguously()
    {
        const int perOwnerCount = 5;
        var dbPath = Path.Combine(Path.GetTempPath(), $"recovery-ledger-concurrency-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={dbPath}";

        try
        {
            await using (var schemaContext = new DlqDbContext(
                new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options))
            {
                await schemaContext.Database.EnsureCreatedAsync();
            }

            async Task RunForOwnerAsync(string ownerId)
            {
                for (var i = 0; i < perOwnerCount; i++)
                {
                    await using var dbContext = new DlqDbContext(
                        new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options);
                    var service = new RecoveryLedgerService(dbContext);

                    var result = await service.OpenOperationAsync(new OpenRecoveryOperationRequest
                    {
                        OwnerId = ownerId,
                        Kind = RecoveryOperationKind.Replay,
                        Trigger = RecoveryTrigger.Manual,
                        Actor = Actor(),
                        ScopeDescription = "entity=orders-dlq",
                        TargetCount = 1,
                    });

                    result.IsSuccess.Should().BeTrue();
                }
            }

            await Task.WhenAll(RunForOwnerAsync(OwnerA), RunForOwnerAsync(OwnerB));

            await using var verifyContext = new DlqDbContext(
                new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(connectionString).Options);

            var ownerASeqs = await verifyContext.RecoveryEvents
                .Where(e => e.OwnerId == OwnerA).Select(e => e.Seq).OrderBy(s => s).ToListAsync();
            var ownerBSeqs = await verifyContext.RecoveryEvents
                .Where(e => e.OwnerId == OwnerB).Select(e => e.Seq).OrderBy(s => s).ToListAsync();

            ownerASeqs.Should().Equal(Enumerable.Range(1, perOwnerCount).Select(i => (long)i));
            ownerBSeqs.Should().Equal(Enumerable.Range(1, perOwnerCount).Select(i => (long)i));

            var verifyService = new RecoveryLedgerService(verifyContext);
            (await verifyService.VerifyChainAsync(OwnerA)).IsValid.Should().BeTrue();
            (await verifyService.VerifyChainAsync(OwnerB)).IsValid.Should().BeTrue();
        }
        finally
        {
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm", "-journal" })
            {
                var path = dbPath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
