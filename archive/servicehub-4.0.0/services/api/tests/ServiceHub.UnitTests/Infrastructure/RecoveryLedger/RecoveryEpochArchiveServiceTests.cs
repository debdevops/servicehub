using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

/// <summary>
/// Proves the roadmap next-chapter M5.2 claim end to end: sealing an epoch archives every prior
/// event to a verified file, prunes exactly those rows from the live table while leaving the
/// seal marker as the next epoch's anchor, and a chain spanning two sealed epochs plus the live
/// table still reconstructs and verifies from the archives alone.
/// </summary>
public sealed class RecoveryEpochArchiveServiceTests : IDisposable
{
    private const string OwnerA = "epoch-owner-a";
    private const string OwnerB = "epoch-owner-b";

    private readonly string _tempRoot;
    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _ledger;
    private readonly RecoveryEpochArchiveService _sut;

    public RecoveryEpochArchiveServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"servicehub-epoch-archive-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _ledger = new RecoveryLedgerService(_dbContext);
        _sut = new RecoveryEpochArchiveService(
            _ledger, _dbContext, new ConfigurationBuilder().Build(),
            Options.Create(new RecoveryEpochArchiveOptions { ArchiveDirectory = _tempRoot }),
            NullLogger<RecoveryEpochArchiveService>.Instance);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static RecoveryActor Actor() => new("test-admin", RecoveryActorKind.User);

    private async Task SeedEntryAsync(string ownerId)
    {
        var operation = await _ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            Actor = Actor(),
            ScopeDescription = "entity=orders-dlq",
            TargetCount = 1,
        });
        var entry = await _ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = operation.Value.Id,
            OwnerId = ownerId,
            Actor = Actor(),
            BodyHash = $"body-{Guid.NewGuid():N}",
            TargetEntity = "orders-dlq",
        });
        await _ledger.RecordExecutionAsync(new RecordExecutionRequest
        {
            EntryId = entry.Value.Id,
            OwnerId = ownerId,
            Actor = Actor(),
            Outcome = RecoveryExecutionOutcome.Accepted,
        });
    }

    [Fact]
    public async Task SealAndArchiveEpochAsync_NoEvents_Fails()
    {
        var result = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RecoveryLedger.EpochSealNothingToSeal");
    }

    [Fact]
    public async Task SealAndArchiveEpochAsync_ArchivesPriorEventsAndKeepsSealMarkerLive()
    {
        await SeedEntryAsync(OwnerA);
        var eventCountBeforeSeal = await _dbContext.RecoveryEvents.CountAsync(e => e.OwnerId == OwnerA);

        var result = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        result.IsSuccess.Should().BeTrue();
        result.Value.EpochNumber.Should().Be(1);
        result.Value.ArchivedEventCount.Should().Be(eventCountBeforeSeal);
        File.Exists(result.Value.ArchiveFilePath).Should().BeTrue();

        // The live table keeps exactly one row for this owner: the seal marker itself.
        var liveEvents = await _dbContext.RecoveryEvents
            .AsNoTracking().Where(e => e.OwnerId == OwnerA).OrderBy(e => e.Seq).ToListAsync();
        liveEvents.Should().ContainSingle();
        liveEvents[0].EventType.Should().Be(RecoveryEventType.EpochSealed);
        // The marker's PrevHash is the archived range's terminal hash — its own EntryHash is a
        // new value (computed over its own fields) that becomes the *next* epoch's anchor.
        liveEvents[0].PrevHash.Should().Be(result.Value.TerminalHash);
    }

    [Fact]
    public async Task SealAndArchiveEpochAsync_ArchiveFileContainsFullVerifiableChain()
    {
        await SeedEntryAsync(OwnerA);

        var result = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        var json = await File.ReadAllTextAsync(result.Value.ArchiveFilePath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        root.GetProperty("ownerId").GetString().Should().Be(OwnerA);
        root.GetProperty("epochNumber").GetInt32().Should().Be(1);
        root.GetProperty("startSeq").GetInt64().Should().Be(1);
        root.GetProperty("startPrevHash").GetString().Should().Be(RecoveryHashChain.GenesisHash);
        var events = root.GetProperty("events").EnumerateArray().ToList();
        events.Should().HaveCount(result.Value.ArchivedEventCount);
        events[^1].GetProperty("entryHash").GetString().Should().Be(result.Value.TerminalHash);
    }

    [Fact]
    public async Task SealAndArchiveEpochAsync_SecondSeal_KeepsFirstMarkerLiveAndChainsFromItsEntryHash()
    {
        await SeedEntryAsync(OwnerA);
        var firstSeal = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());
        firstSeal.IsSuccess.Should().BeTrue();

        var firstMarker = await _dbContext.RecoveryEvents.AsNoTracking()
            .SingleAsync(e => e.OwnerId == OwnerA && e.EventType == RecoveryEventType.EpochSealed);

        await SeedEntryAsync(OwnerA); // new activity in the second epoch
        var secondSeal = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        secondSeal.IsSuccess.Should().BeTrue();
        secondSeal.Value.EpochNumber.Should().Be(2);

        // Epoch 2's archive starts right after the first seal marker (chained from the marker's
        // own EntryHash, since the marker is the immediately preceding live event) — the marker
        // itself is excluded, not archived.
        var epoch2Json = await File.ReadAllTextAsync(secondSeal.Value.ArchiveFilePath);
        using var epoch2Document = JsonDocument.Parse(epoch2Json);
        epoch2Document.RootElement.GetProperty("startPrevHash").GetString().Should().Be(firstMarker.EntryHash);

        // Both seal markers survive live — every historical epoch boundary stays queryable from
        // the live table without opening an archive file, and only the bulk business events
        // between consecutive markers are ever archived and pruned.
        var liveEvents = await _dbContext.RecoveryEvents
            .AsNoTracking().Where(e => e.OwnerId == OwnerA).OrderBy(e => e.Seq).ToListAsync();
        liveEvents.Should().HaveCount(2);
        liveEvents[0].Id.Should().Be(firstMarker.Id);
        liveEvents[1].EventType.Should().Be(RecoveryEventType.EpochSealed);
        liveEvents[1].PrevHash.Should().Be(secondSeal.Value.TerminalHash);
    }

    [Fact]
    public async Task SealAndArchiveEpochAsync_NothingSinceLastSeal_Fails()
    {
        await SeedEntryAsync(OwnerA);
        await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        var result = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RecoveryLedger.EpochSealNothingSinceLastSeal");
    }

    [Fact]
    public async Task SealAndArchiveEpochAsync_ScopesToOwner_DoesNotTouchAnotherOwnersEvents()
    {
        await SeedEntryAsync(OwnerA);
        await SeedEntryAsync(OwnerB);

        await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        var ownerBEventCount = await _dbContext.RecoveryEvents.CountAsync(e => e.OwnerId == OwnerB);
        ownerBEventCount.Should().BeGreaterThan(0);

        // Owner B was never sealed, so none of its rows were pruned.
        var ownerBRaw = await _ledger.VerifyChainAsync(OwnerB);
        ownerBRaw.IsValid.Should().BeTrue();
        ownerBRaw.EventsChecked.Should().Be(ownerBEventCount);
    }

    [Fact]
    public async Task SealAndArchiveEpochAsync_TamperedRangeBeforeSeal_FailsWithoutPersistingSealMarker()
    {
        await SeedEntryAsync(OwnerA);

        // RecoveryEvent is entirely init-only in EF Core (append-only guard, by design), so
        // simulating tampering means going around EF entirely — a raw UPDATE against the live
        // table, the same technique BackupRestoreVerificationTests' own tamper test uses.
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE RecoveryEvents SET EventType = {nameof(RecoveryEventType.ProviderRejected)} WHERE Id = (SELECT Id FROM RecoveryEvents WHERE OwnerId = {OwnerA} ORDER BY Seq LIMIT 1)");

        var eventsBefore = await _dbContext.RecoveryEvents
            .AsNoTracking().Where(e => e.OwnerId == OwnerA).ToListAsync();

        var result = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        // Regression (Copilot finding): sealing used to persist the EpochSealed marker before
        // verifying the range, so a tampered/corrupt range left an unarchivable marker behind
        // even though this contract promises "fails, and changes nothing."
        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RecoveryEpochArchive.RangeDoesNotVerify");

        var liveEvents = await _dbContext.RecoveryEvents
            .AsNoTracking().Where(e => e.OwnerId == OwnerA).ToListAsync();
        liveEvents.Should().BeEquivalentTo(eventsBefore);
        liveEvents.Should().NotContain(e => e.EventType == RecoveryEventType.EpochSealed);
    }

    [Fact]
    public async Task SealAndArchiveEpochAsync_ChainRemainsVerifiableAcrossTheLiveAnchor()
    {
        await SeedEntryAsync(OwnerA);
        var seal = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        await SeedEntryAsync(OwnerA); // new activity continues from the seal marker

        var liveEvents = await _dbContext.RecoveryEvents
            .AsNoTracking().Where(e => e.OwnerId == OwnerA).OrderBy(e => e.Seq).ToListAsync();

        // Starting the verifier at the seal marker's own Seq/PrevHash (rather than assuming
        // genesis) proves the live table alone still self-verifies after a prune.
        var result = RecoveryChainVerifier.Verify(OwnerA, liveEvents, liveEvents[0].Seq, liveEvents[0].PrevHash);
        result.IsValid.Should().BeTrue();
        liveEvents[0].PrevHash.Should().Be(seal.Value.TerminalHash);
    }

    [Fact]
    public async Task SealAndArchiveEpochAsync_ArchiveRecordsItsOwnSealMarkersSeqAndHash()
    {
        await SeedEntryAsync(OwnerA);
        var seal = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        var marker = await _dbContext.RecoveryEvents.AsNoTracking()
            .SingleAsync(e => e.OwnerId == OwnerA && e.EventType == RecoveryEventType.EpochSealed);

        var json = await File.ReadAllTextAsync(seal.Value.ArchiveFilePath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // Regression (Copilot finding): the archive must record the live seal marker's own
        // Seq/EntryHash, not just endSeq/terminalHash — an offline verifier chaining archives (or
        // an archive into the live export) needs this to bridge the one-Seq gap the marker
        // occupies without ever being archived itself.
        root.GetProperty("sealEventSeq").GetInt64().Should().Be(marker.Seq);
        root.GetProperty("sealEventHash").GetString().Should().Be(marker.EntryHash);
    }

    [Fact]
    public async Task VerifyChainAsync_StillValidAfterTwoSealedEpochs()
    {
        await SeedEntryAsync(OwnerA);
        await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        await SeedEntryAsync(OwnerA);
        await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        await SeedEntryAsync(OwnerA); // trailing, not-yet-sealed activity

        // Regression (Copilot finding): the live table now holds two surviving seal markers with
        // a Seq gap before each (everything between them was archived and pruned). The public
        // /verify endpoint must not treat that gap as tampering.
        var result = await _ledger.VerifyChainAsync(OwnerA);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task SealAndArchiveEpochAsync_TamperedFirstEventPrevHash_ReHashedToMatchItself_IsStillDetected()
    {
        await SeedEntryAsync(OwnerA);

        // Simulate a tampered first event whose PrevHash was forged to an arbitrary value and
        // then re-hashed so its own EntryHash is internally self-consistent with the forgery —
        // and every later event in the range re-chained from it, so the *entire* forged range is
        // internally consistent. This isolates the exact attack the pre-fix tautological
        // verification (seeding the verifier with the range's own first PrevHash instead of the
        // trusted previous anchor) could not catch: without re-chaining every later event too, a
        // downstream PrevHash mismatch would catch the tamper for an unrelated reason.
        var events = await _dbContext.RecoveryEvents.AsNoTracking()
            .Where(e => e.OwnerId == OwnerA).OrderBy(e => e.Seq).ToListAsync();

        var forgedPrevHash = new string('a', 64);
        foreach (var evt in events)
        {
            var forgedEntryHash = RecoveryHashChain.ComputeEntryHash(
                evt.Id, evt.OwnerId, evt.Seq, evt.EntryId, evt.OperationId,
                evt.EventType, evt.OccurredAt, evt.ActorIdentity, evt.ActorKind,
                evt.DetailJson, evt.SchemaVersion, forgedPrevHash);

            await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE RecoveryEvents SET PrevHash = {forgedPrevHash}, EntryHash = {forgedEntryHash} WHERE Id = {evt.Id}");

            forgedPrevHash = forgedEntryHash;
        }

        var result = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("RecoveryEpochArchive.RangeDoesNotVerify");

        var liveEvents = await _dbContext.RecoveryEvents
            .AsNoTracking().Where(e => e.OwnerId == OwnerA).ToListAsync();
        liveEvents.Should().NotContain(e => e.EventType == RecoveryEventType.EpochSealed);
    }
}
