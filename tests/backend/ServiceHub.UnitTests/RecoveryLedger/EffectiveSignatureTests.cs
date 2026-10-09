using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;
using ServiceHub.Infrastructure.Signatures;

namespace ServiceHub.UnitTests.RecoveryLedger;

/// <summary>
/// Design 10 §6 (owner chose R2, 2026-10-08): after the re-sign, each replay counts under the signature its message carries now,
/// so a split signature inherits only its own cause's history — never the other cause's.
/// </summary>
public sealed class EffectiveSignatureTests : IDisposable
{
    private const string Owner = "owner1";
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly ServiceHubDbContext _db;
    private readonly RecoveryLedgerService _ledger;
    private readonly Guid _ns = Guid.NewGuid();
    private long _seq;
    private int _minute;

    public EffectiveSignatureTests()
    {
        _db = new ServiceHubDbContext(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite("DataSource=:memory:").Options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _ledger = new RecoveryLedgerService(_db);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    /// <summary>A message signed the version-1 way, replayed once, with the given result — as 4.1.0 leaves the database.</summary>
    private async Task<(DlqMessage Message, string V1)> Replayed(string error, RecoveryDisposition result)
    {
        var at = T0.AddMinutes(_minute++);
        var m = new DlqMessage
        {
            MessageId = Guid.NewGuid().ToString(), SequenceNumber = _seq++, BodyHash = Guid.NewGuid().ToString("N"),
            NamespaceId = _ns, OwnerId = Owner, EntityName = "orders", EntityType = ServiceBusEntityType.Queue,
            CloudProvider = CloudProviderType.Azure, EnqueuedTimeUtc = at, DetectedAtUtc = at,
            DeadLetterReason = "MaxDeliveryCountExceeded", DeadLetterErrorDescription = error,
        };
        m.SignatureHash = await Infrastructure.Signatures.LegacySigner.AssignAsync(_db, m, default);
        _db.DlqMessages.Add(m);
        await _db.SaveChangesAsync();

        var op = new RecoveryOperation
        {
            OwnerId = Owner, Kind = RecoveryOperationKind.Replay, Trigger = RecoveryTrigger.AutoRule, ActorIdentity = "rule",
            ActorKind = RecoveryActorKind.Automation, ScopeDescription = "t", ServiceVersion = "t", OpenedAt = at, TargetCount = 1,
        };
        _db.RecoveryOperations.Add(op);
        _db.RecoveryLedgerEntries.Add(new RecoveryLedgerEntry
        {
            OperationId = op.Id, OwnerId = Owner, BodyHash = m.BodyHash, TargetEntity = "orders", BegunAt = at, NamespaceId = _ns,
            DlqMessageId = m.Id, SignatureHashSnapshot = m.SignatureHash, Disposition = result, ClosedAt = at,
            ProviderSnapshot = CloudProviderType.Azure,
        });
        await _db.SaveChangesAsync();
        return (m, m.SignatureHash!);
    }

    private async Task<(string Cured, string Poison)> TwoCauses(int cured, int poison)
    {
        for (var i = 0; i < cured; i++) await Replayed($"Inventory service timed out after {i} seconds", RecoveryDisposition.Recovered);
        for (var i = 0; i < poison; i++) await Replayed($"Customer {i} does not exist", RecoveryDisposition.Returned);
        await SignatureResigner.ResignNamespaceAsync(_db, Owner, _ns, dryRun: false);
        var hashes = await _db.DlqMessages.AsNoTracking().OrderBy(m => m.SequenceNumber).Select(m => m.SignatureHash!).ToListAsync();
        return (hashes.First(), hashes.Last());
    }

    private async Task<Dictionary<RecoveryDisposition, int>> Counts(string hash) =>
        (await _ledger.GetDispositionCountsAsync(Owner, hash, RecoveryOperationKind.Replay)).ToDictionary(x => x.Key, x => x.Value);

    [Fact]
    public async Task Before_any_re_sign_the_answers_are_exactly_what_they_were()
    {
        var (_, v1) = await Replayed("Inventory timed out after 1 seconds", RecoveryDisposition.Recovered);
        await Replayed("Customer 5 does not exist", RecoveryDisposition.Returned);

        var counts = await Counts(v1);

        counts[RecoveryDisposition.Recovered].Should().Be(1);
        counts[RecoveryDisposition.Returned].Should().Be(1, "version 1 merged the two causes — the bug design 10 fixes");
    }

    [Fact]
    public async Task After_the_re_sign_each_cause_keeps_only_its_own_history()
    {
        var (cured, poison) = await TwoCauses(cured: 4, poison: 3);

        cured.Should().NotBe(poison);
        var curedCounts = await Counts(cured);
        curedCounts[RecoveryDisposition.Recovered].Should().Be(4);
        curedCounts.GetValueOrDefault(RecoveryDisposition.Returned).Should().Be(0);
        var poisonCounts = await Counts(poison);
        poisonCounts[RecoveryDisposition.Returned].Should().Be(3);
        poisonCounts.GetValueOrDefault(RecoveryDisposition.Recovered).Should().Be(0);
    }

    [Fact]
    public async Task The_old_merged_hash_stops_counting_for_anyone()
    {
        var (_, v1) = await Replayed("Inventory timed out after 1 seconds", RecoveryDisposition.Recovered);
        await Replayed("Customer 5 does not exist", RecoveryDisposition.Returned);
        await SignatureResigner.ResignNamespaceAsync(_db, Owner, _ns, dryRun: false);

        (await Counts(v1)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_replay_whose_message_is_gone_keeps_its_old_hash_and_counts_for_no_live_signature()
    {
        var (gone, v1) = await Replayed("Inventory timed out after 1 seconds", RecoveryDisposition.Recovered);
        await Replayed("Customer 5 does not exist", RecoveryDisposition.Returned);
        await SignatureResigner.ResignNamespaceAsync(_db, Owner, _ns, dryRun: false);
        var live = await _db.DlqMessages.AsNoTracking().Select(m => m.SignatureHash!).ToListAsync();
        await _db.DlqMessages.Where(m => m.Id == gone.Id).ExecuteDeleteAsync();

        (await Counts(v1)).Should().ContainKey(RecoveryDisposition.Recovered, "its snapshot is all that is left of it");
        foreach (var h in live.Distinct())
        {
            (await Counts(h)).GetValueOrDefault(RecoveryDisposition.Recovered).Should().Be(0);
        }
    }

    [Fact]
    public async Task The_signatures_the_trust_evaluator_visits_are_the_live_ones()
    {
        var (cured, poison) = await TwoCauses(2, 2);

        var visited = await _ledger.GetDistinctSignatureHashesAsync(Owner, RecoveryOperationKind.Replay);

        visited.Should().BeEquivalentTo([cured, poison]);
    }

    [Fact]
    public async Task Provider_lookup_follows_the_new_signature()
    {
        var (cured, _) = await TwoCauses(1, 1);
        (await _ledger.GetSignatureProviderAsync(Owner, cured)).Should().Be(CloudProviderType.Azure);
    }
}
