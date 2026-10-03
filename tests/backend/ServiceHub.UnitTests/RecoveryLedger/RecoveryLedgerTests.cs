using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.UnitTests.Architecture;

namespace ServiceHub.UnitTests.RecoveryLedger;

/// <summary>
/// Unit 2.5's "done when": an entry written by 4.1.0 is verified by the offline script, and an attempted
/// update throws. Plus the state machine that decides what each entry is allowed to become.
/// </summary>
public sealed class RecoveryLedgerTests : IDisposable
{
    private const string Owner = "owner-1";
    private static readonly RecoveryActor Actor = new("session", RecoveryActorKind.User);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceHubDbContext _db;

    public RecoveryLedgerTests()
    {
        _connection.Open();
        _db = NewContext();
        _db.Database.Migrate();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private ServiceHubDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite(_connection).Options);

    private RecoveryLedgerService Ledger() => new(_db);

    private async Task<RecoveryLedgerEntry> BeganEntry(RecoveryLedgerService ledger, RecoveryOperationKind kind = RecoveryOperationKind.Replay)
    {
        var op = (await ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = Owner, Kind = kind, Trigger = RecoveryTrigger.Manual, Actor = Actor,
            Reason = "test", ScopeDescription = "entity=orders", TargetCount = 1,
        })).Value;
        return (await ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = op.Id, OwnerId = Owner, Actor = Actor, BodyHash = "abc", TargetEntity = "orders",
        })).Value;
    }

    private static RecordExecutionRequest Execution(Guid entryId, RecoveryExecutionOutcome outcome) =>
        new() { EntryId = entryId, OwnerId = Owner, Actor = Actor, Outcome = outcome };

    [Fact]
    public void There_are_ten_entry_states_and_the_two_honest_ones_exist()
    {
        Enum.GetValues<RecoveryEntryState>().Should().HaveCount(11); // ten states plus Declined
        Enum.GetNames<RecoveryEntryState>().Should().Contain(["Unverified", "ExecutionUnknown"]);
    }

    [Fact]
    public async Task A_replay_walks_executing_to_observing_to_recovered_and_the_chain_holds()
    {
        var ledger = Ledger();
        var entry = await BeganEntry(ledger);
        entry.State.Should().Be(RecoveryEntryState.Executing);

        var observing = (await ledger.RecordExecutionAsync(Execution(entry.Id, RecoveryExecutionOutcome.Accepted))).Value;
        observing.State.Should().Be(RecoveryEntryState.Observing);
        observing.ObservationWindowEndsAt.Should().NotBeNull();

        var done = (await ledger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id, OwnerId = Owner, Actor = Actor, Outcome = RecoveryObservationOutcome.NoRecurrenceObserved,
        })).Value;
        done.State.Should().Be(RecoveryEntryState.Recovered);
        done.Disposition.Should().Be(RecoveryDisposition.Recovered);
        done.ClosedAt.Should().NotBeNull();

        var chain = await ledger.VerifyChainAsync(Owner);
        chain.IsValid.Should().BeTrue();
        chain.EventsChecked.Should().Be(5); // opened, begun, accepted, window opened, no recurrence
    }

    [Fact]
    public async Task A_rejected_call_is_terminal_and_a_purge_is_discarded()
    {
        var ledger = Ledger();
        var rejected = (await ledger.RecordExecutionAsync(Execution((await BeganEntry(ledger)).Id, RecoveryExecutionOutcome.Rejected))).Value;
        rejected.State.Should().Be(RecoveryEntryState.ExecutionFailed);
        rejected.Disposition.Should().Be(RecoveryDisposition.Failed);

        var purged = (await ledger.RecordExecutionAsync(Execution((await BeganEntry(ledger, RecoveryOperationKind.Purge)).Id, RecoveryExecutionOutcome.Accepted))).Value;
        purged.State.Should().Be(RecoveryEntryState.Discarded);
    }

    [Fact]
    public async Task An_unknown_outcome_stays_open_rather_than_pretending()
    {
        var ledger = Ledger();
        var entry = (await ledger.RecordExecutionAsync(Execution((await BeganEntry(ledger)).Id, RecoveryExecutionOutcome.Unknown))).Value;
        entry.State.Should().Be(RecoveryEntryState.ExecutionUnknown);
        entry.ClosedAt.Should().BeNull();
    }

    [Fact]
    public async Task An_illegal_transition_is_a_failure_not_an_exception()
    {
        var ledger = Ledger();
        var entry = await BeganEntry(ledger);

        var early = await ledger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id, OwnerId = Owner, Actor = Actor, Outcome = RecoveryObservationOutcome.NoRecurrenceObserved,
        });
        early.IsFailure.Should().BeTrue();

        await ledger.RecordExecutionAsync(Execution(entry.Id, RecoveryExecutionOutcome.Rejected));
        (await ledger.RecordExecutionAsync(Execution(entry.Id, RecoveryExecutionOutcome.Accepted))).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task A_recurrence_needs_a_confidence_and_a_purge_needs_a_reason()
    {
        var ledger = Ledger();
        var entry = await BeganEntry(ledger);
        await ledger.RecordExecutionAsync(Execution(entry.Id, RecoveryExecutionOutcome.Accepted));

        (await ledger.RecordObservationAsync(new RecordObservationRequest
        {
            EntryId = entry.Id, OwnerId = Owner, Actor = Actor, Outcome = RecoveryObservationOutcome.RecurrenceObserved,
        })).IsFailure.Should().BeTrue();

        (await ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = Owner, Kind = RecoveryOperationKind.Purge, Trigger = RecoveryTrigger.Manual, Actor = Actor,
            ScopeDescription = "x", TargetCount = 1,
        })).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Writing_off_needs_a_reason_and_only_an_open_entry_can_be_written_off()
    {
        var ledger = Ledger();
        var entry = await BeganEntry(ledger);

        (await ledger.CloseAsync(entry.Id, Owner, Actor, " ")).IsFailure.Should().BeTrue();
        (await ledger.CloseAsync(entry.Id, Owner, Actor, "unrecoverable")).Value.State.Should().Be(RecoveryEntryState.WrittenOff);
        (await ledger.CloseAsync(entry.Id, Owner, Actor, "again")).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Another_owner_cannot_see_or_move_an_entry()
    {
        var ledger = Ledger();
        var entry = await BeganEntry(ledger);

        (await ledger.GetEntryAsync(entry.Id, "someone-else")).Should().BeNull();
        var result = await ledger.RecordExecutionAsync(
            new RecordExecutionRequest { EntryId = entry.Id, OwnerId = "someone-else", Actor = Actor, Outcome = RecoveryExecutionOutcome.Accepted });
        result.IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task An_attempted_update_or_delete_of_evidence_throws()
    {
        var ledger = Ledger();
        var entry = await BeganEntry(ledger);
        await ledger.RecordExecutionAsync(Execution(entry.Id, RecoveryExecutionOutcome.Accepted));

        using (var db = NewContext())
        {
            var evt = await db.RecoveryEvents.FirstAsync();
            db.Entry(evt).Property(e => e.DetailJson).CurrentValue = "edited";
            db.Entry(evt).Property(e => e.DetailJson).IsModified = true;
            await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>();
        }

        using (var db = NewContext())
        {
            db.RecoveryEvents.Remove(await db.RecoveryEvents.FirstAsync());
            await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>();
        }

        using (var db = NewContext())
        {
            db.RecoveryLedgerEntries.Remove(await db.RecoveryLedgerEntries.FirstAsync());
            await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>();
        }

        using (var db = NewContext())
        {
            var row = await db.RecoveryLedgerEntries.FirstAsync();
            db.Entry(row).Property(e => e.BodyHash).CurrentValue = "changed";
            db.Entry(row).Property(e => e.BodyHash).IsModified = true;
            await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().ThrowAsync<InvalidOperationException>().WithMessage("*BodyHash*");
        }
    }

    [Fact]
    public void Every_entry_property_is_classified_as_immutable_or_mutable()
    {
        var mutable = RecoveryLedgerAppendOnlyGuard.MutableEntryProperties;
        typeof(RecoveryLedgerEntry).GetProperties().Select(p => p.Name).Should().Contain(mutable);
    }

    [Fact]
    public async Task A_direct_edit_of_the_file_is_caught_by_the_chain_check()
    {
        var ledger = Ledger();
        await BeganEntry(ledger);
        (await ledger.VerifyChainAsync(Owner)).IsValid.Should().BeTrue();

        // Straight SQL — beneath EF, so the append-only guard cannot see it; the chain must.
        await _db.Database.ExecuteSqlRawAsync("UPDATE RecoveryEvents SET ActorIdentity = 'someone-else' WHERE Seq = 1");

        var result = await ledger.VerifyChainAsync(Owner);
        result.IsValid.Should().BeFalse();
        result.FirstDivergentSeq.Should().Be(1);
    }

    [Fact]
    public async Task The_offline_script_verifies_what_4_1_0_wrote_and_catches_an_edit()
    {
        var ledger = Ledger();
        var entry = await BeganEntry(ledger);
        await ledger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Id, OwnerId = Owner, Actor = Actor, Outcome = RecoveryExecutionOutcome.Accepted,
            ProviderDetailJson = "{\"note\":\"accepted\"}",
        });

        var events = await _db.RecoveryEvents.AsNoTracking().Where(e => e.OwnerId == Owner).OrderBy(e => e.Seq).ToListAsync();
        var exported = events.Select(e => new Dictionary<string, object?>
        {
            ["id"] = e.Id, ["ownerId"] = e.OwnerId, ["seq"] = e.Seq, ["entryId"] = e.EntryId, ["operationId"] = e.OperationId,
            ["eventType"] = e.EventType.ToString(), ["occurredAt"] = e.OccurredAt.ToUniversalTime().ToString("O"),
            ["actorIdentity"] = e.ActorIdentity, ["actorKind"] = e.ActorKind.ToString(), ["detailJson"] = e.DetailJson,
            ["schemaVersion"] = e.SchemaVersion, ["prevHash"] = e.PrevHash, ["entryHash"] = e.EntryHash,
        }).ToList();

        var dir = Path.Combine(Path.GetTempPath(), $"chain-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var file = Path.Combine(dir, "events.json");
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(exported));
            RunScript(file).Should().Be((0, true));

            exported[1]["actorIdentity"] = "forged";
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(exported));
            RunScript(file).ExitCode.Should().Be(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static (int ExitCode, bool Passed) RunScript(string eventsFile)
    {
        var script = Path.Combine(RepositoryRoot.Directory.FullName, "scripts", "verify-recovery-chain.py");
        var start = new ProcessStartInfo("python3", $"\"{script}\" \"{eventsFile}\"")
        {
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.StartsWith("PASS", StringComparison.Ordinal));
    }
}
