using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Proves the roadmap next-chapter M5.1 claim in CI rather than leaving it as an undocumented
/// belief: a database created at the last migration that shipped in v3.7.0
/// (<c>20260818183029_AddAutoReplayRuleDisabledReason</c> — the newest migration older than the
/// v3.7.0 tag date of 2026-08-19, per <c>CHANGELOG.md</c>) can be upgraded in place, through every
/// migration the unreleased chapter and this next one have added since, without losing data or
/// breaking the Recovery Evidence Ledger's hash chain. An untested upgrade path is a belief, not a
/// control — the same reasoning <c>BackupRestoreVerificationTests</c> applies to restore.
/// </summary>
public sealed class UpgradeInPlaceTests : IDisposable
{
    private const string V370EraMigration = "20260818183029_AddAutoReplayRuleDisabledReason";
    private const string OwnerA = "upgrade-owner-a";

    private readonly string _tempRoot;
    private readonly string _dbPath;

    public UpgradeInPlaceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"servicehub-upgrade-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _dbPath = Path.Combine(_tempRoot, "servicehub-dlq.db");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private DlqDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        return new DlqDbContext(options);
    }

    [Fact]
    public async Task V370EraDatabase_MigratedToLatest_PreservesDataAndVerifiesChain()
    {
        // ── Stand up a database at exactly the last migration that shipped in v3.7.0 — a v3.7-era
        //    deployment's schema, not today's. ──
        await using (var v370Context = CreateContext())
        {
            var migrator = v370Context.GetService<IMigrator>();
            await migrator.MigrateAsync(V370EraMigration);
        }

        // ── Seed a real hash-chained ledger against that old schema, exactly as a v3.7 deployment
        //    would have — RecoveryEvents/RecoveryOperations/RecoveryLedgerEntries all existed
        //    then. ──
        int entriesBeforeUpgrade;
        await using (var v370Context = CreateContext())
        {
            var ledger = new RecoveryLedgerService(v370Context);
            var operation = await ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
            {
                OwnerId = OwnerA,
                Kind = RecoveryOperationKind.Replay,
                Trigger = RecoveryTrigger.Manual,
                Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
                ScopeDescription = "entity=orders-dlq",
                TargetCount = 2,
            });
            operation.IsSuccess.Should().BeTrue();

            for (var i = 0; i < 2; i++)
            {
                var entry = await ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
                {
                    OperationId = operation.Value.Id,
                    OwnerId = OwnerA,
                    Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
                    BodyHash = $"body-hash-{i}",
                    TargetEntity = "orders-dlq",
                });
                entry.IsSuccess.Should().BeTrue();

                var execution = await ledger.RecordExecutionAsync(new RecordExecutionRequest
                {
                    EntryId = entry.Value.Id,
                    OwnerId = OwnerA,
                    Actor = new RecoveryActor("test-actor", RecoveryActorKind.User),
                    Outcome = RecoveryExecutionOutcome.Accepted,
                });
                execution.IsSuccess.Should().BeTrue();
            }

            entriesBeforeUpgrade = await v370Context.RecoveryLedgerEntries.CountAsync(e => e.OwnerId == OwnerA);
        }
        entriesBeforeUpgrade.Should().Be(2);

        // ── Upgrade in place: migrate the same file all the way to HEAD — every migration the
        //    unreleased autonomy chapter and this next chapter (M1/M2/M3) added. ──
        await using (var upgradeContext = CreateContext())
        {
            var migrator = upgradeContext.GetService<IMigrator>();
            await migrator.MigrateAsync();
        }

        // ── No data lost across the upgrade ──
        await using (var verifyContext = CreateContext())
        {
            (await verifyContext.RecoveryLedgerEntries.CountAsync(e => e.OwnerId == OwnerA)).Should().Be(2);

            // ── The hash chain written under the old schema still verifies after upgrading —
            //    "evidence you cannot verify after upgrading is not evidence" (roadmap M5.3). ──
            var ledger = new RecoveryLedgerService(verifyContext);
            var chain = await ledger.VerifyChainAsync(OwnerA);
            chain.IsValid.Should().BeTrue();

            // ── The schema is genuinely current: tables this next chapter's migrations added are
            //    queryable, not just present in the migrations history table. ──
            verifyContext.Anomalies.Should().NotBeNull();
            (await verifyContext.Anomalies.CountAsync()).Should().Be(0);
            (await verifyContext.ProductionElevations.CountAsync()).Should().Be(0);
            (await verifyContext.DlqObserverAttestations.CountAsync()).Should().Be(0);
        }
    }
}
