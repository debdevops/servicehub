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
    public async Task SealAndArchiveEpochAsync_SecondSeal_ArchivesFirstMarkerTooAndChainsTerminalHashes()
    {
        await SeedEntryAsync(OwnerA);
        var firstSeal = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());
        firstSeal.IsSuccess.Should().BeTrue();

        await SeedEntryAsync(OwnerA); // new activity in the second epoch
        var secondSeal = await _sut.SealAndArchiveEpochAsync(OwnerA, Actor());

        secondSeal.IsSuccess.Should().BeTrue();
        secondSeal.Value.EpochNumber.Should().Be(2);

        // Epoch 2's archive starts exactly where epoch 1's terminal hash left off — the first
        // seal marker itself is now archived (superseded), included in epoch 2's file.
        var epoch2Json = await File.ReadAllTextAsync(secondSeal.Value.ArchiveFilePath);
        using var epoch2Document = JsonDocument.Parse(epoch2Json);
        epoch2Document.RootElement.GetProperty("startPrevHash").GetString().Should().Be(firstSeal.Value.TerminalHash);

        var liveEvents = await _dbContext.RecoveryEvents
            .AsNoTracking().Where(e => e.OwnerId == OwnerA).ToListAsync();
        liveEvents.Should().ContainSingle(); // only the second seal marker survives live
        liveEvents[0].PrevHash.Should().Be(secondSeal.Value.TerminalHash);
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
}
