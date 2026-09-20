using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Microsoft.Data.Sqlite;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Models.Backup;
using ServiceHub.Infrastructure.Backup;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.Backup;

/// <summary>
/// Proves the roadmap W0.4 claim end to end, in CI, rather than leaving it as a documented
/// operator procedure nobody has exercised: seed a real hash-chained recovery ledger into a
/// file-backed SQLite database, back it up with the production <see cref="BackupService"/>
/// (real <c>VACUUM INTO</c>, real manifest + checksum), restore the bundle's database file into
/// a brand-new location standing in for a clean container, and re-verify the ledger hash chain
/// against that restored file with a fresh <see cref="RecoveryLedgerService"/> instance that
/// never saw the original database. An untested backup is a belief, not a control.
/// </summary>
public sealed class BackupRestoreVerificationTests : IDisposable
{
    private const string OwnerA = "restore-owner-a";
    private const string OwnerB = "restore-owner-b";
    private const string FingerprintValue = "sha256:restore-test-fingerprint";

    private readonly string _tempRoot;
    private readonly string _sourceDbPath;
    private readonly string _backupDir;
    private readonly string _restoredDbPath;

    public BackupRestoreVerificationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"servicehub-restore-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);
        _sourceDbPath = Path.Combine(_tempRoot, "source", "servicehub-dlq.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_sourceDbPath)!);
        _backupDir = Path.Combine(_tempRoot, "backups");
        _restoredDbPath = Path.Combine(_tempRoot, "restored-clean-container", "servicehub-dlq.db");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private static RecoveryActor Actor() => new("test-actor", RecoveryActorKind.User);

    private static async Task SeedLedgerAsync(DlqDbContext dbContext, string ownerId, int entryCount)
    {
        var service = new RecoveryLedgerService(dbContext);

        var operation = await service.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = ownerId,
            Kind = RecoveryOperationKind.Replay,
            Trigger = RecoveryTrigger.Manual,
            Actor = Actor(),
            ScopeDescription = "entity=orders-dlq",
            TargetCount = entryCount,
        });
        operation.IsSuccess.Should().BeTrue();

        for (var i = 0; i < entryCount; i++)
        {
            var entry = await service.BeginEntryAsync(new BeginRecoveryEntryRequest
            {
                OperationId = operation.Value.Id,
                OwnerId = ownerId,
                Actor = Actor(),
                BodyHash = $"body-hash-{i}",
                TargetEntity = "orders-dlq",
            });
            entry.IsSuccess.Should().BeTrue();

            var execution = await service.RecordExecutionAsync(new RecordExecutionRequest
            {
                EntryId = entry.Value.Id,
                OwnerId = ownerId,
                Actor = Actor(),
                Outcome = RecoveryExecutionOutcome.Accepted,
            });
            execution.IsSuccess.Should().BeTrue();
        }
    }

    [Fact]
    public async Task BackupThenRestoreIntoCleanContainer_ReVerifiesHashChainEndToEnd()
    {
        // ── Seed a real hash-chained ledger for two owners, against a real file-backed DB ──
        var sourceConnectionString = $"Data Source={_sourceDbPath}";
        await using (var schemaContext = new DlqDbContext(
            new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(sourceConnectionString).Options))
        {
            await schemaContext.Database.EnsureCreatedAsync();
            await SeedLedgerAsync(schemaContext, OwnerA, entryCount: 3);
            await SeedLedgerAsync(schemaContext, OwnerB, entryCount: 2);
        }

        int ownerAEventsBeforeBackup;
        int ownerBEventsBeforeBackup;
        await using (var countContext = new DlqDbContext(
            new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(sourceConnectionString).Options))
        {
            ownerAEventsBeforeBackup = await countContext.RecoveryEvents.CountAsync(e => e.OwnerId == OwnerA);
            ownerBEventsBeforeBackup = await countContext.RecoveryEvents.CountAsync(e => e.OwnerId == OwnerB);
        }
        ownerAEventsBeforeBackup.Should().BeGreaterThan(0);
        ownerBEventsBeforeBackup.Should().BeGreaterThan(0);

        // ── Back it up with the real BackupService (VACUUM INTO + manifest + checksum) ──
        BackupManifest manifest;
        await using (var backupSourceContext = new DlqDbContext(
            new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(sourceConnectionString).Options))
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["NamespaceRepository:DataDirectory"] = Path.Combine(_tempRoot, "no-namespace-store-here"),
                })
                .Build();
            var backupOptions = Options.Create(new BackupOptions { BackupDirectory = _backupDir, RetentionCount = 14 });
            var protectorMock = new Mock<IConnectionStringProtector>();
            protectorMock.Setup(p => p.GetKeyFingerprint()).Returns(FingerprintValue);

            var backupService = new BackupService(
                backupSourceContext, protectorMock.Object, configuration, backupOptions, NullLogger<BackupService>.Instance);

            var backupResult = await backupService.CreateBackupAsync();
            backupResult.IsSuccess.Should().BeTrue();
            manifest = backupResult.Value;
        }

        manifest.IntegrityCheck.Should().Be("ok");

        // ── Runbook step: recompute the snapshot's SHA-256 and compare against the manifest ──
        var backedUpDbPath = Path.Combine(_backupDir, manifest.BackupId, "servicehub-dlq.db");
        File.Exists(backedUpDbPath).Should().BeTrue();
        var recomputedHash = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(await File.ReadAllBytesAsync(backedUpDbPath))).ToLowerInvariant();
        recomputedHash.Should().Be(manifest.Sqlite.Sha256);

        // ── Restore: copy the verified snapshot into a brand-new location — a clean container ──
        Directory.CreateDirectory(Path.GetDirectoryName(_restoredDbPath)!);
        File.Copy(backedUpDbPath, _restoredDbPath);

        // ── Re-verify the ledger hash chain end to end, from a fresh service instance that never
        //    saw the original database — this is the actual proof, not just "the file copied". ──
        var restoredConnectionString = $"Data Source={_restoredDbPath}";
        await using var restoredContext = new DlqDbContext(
            new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(restoredConnectionString).Options);
        var restoredLedger = new RecoveryLedgerService(restoredContext);

        var chainA = await restoredLedger.VerifyChainAsync(OwnerA);
        var chainB = await restoredLedger.VerifyChainAsync(OwnerB);
        chainA.IsValid.Should().BeTrue();
        chainB.IsValid.Should().BeTrue();

        // ── No data loss: the restored evidence ledger has exactly what was seeded ──
        (await restoredContext.RecoveryEvents.CountAsync(e => e.OwnerId == OwnerA)).Should().Be(ownerAEventsBeforeBackup);
        (await restoredContext.RecoveryEvents.CountAsync(e => e.OwnerId == OwnerB)).Should().Be(ownerBEventsBeforeBackup);
        (await restoredContext.RecoveryLedgerEntries.CountAsync(e => e.OwnerId == OwnerA)).Should().Be(3);
        (await restoredContext.RecoveryLedgerEntries.CountAsync(e => e.OwnerId == OwnerB)).Should().Be(2);
    }

    [Fact]
    public async Task RestoredDatabase_TamperedAfterRestore_ChainVerificationDetectsIt()
    {
        // ── Negative control: prove VerifyChainAsync would actually catch a corrupted restore,
        //    not just rubber-stamp anything that opens as a valid SQLite file. ──
        var sourceConnectionString = $"Data Source={_sourceDbPath}";
        await using (var schemaContext = new DlqDbContext(
            new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(sourceConnectionString).Options))
        {
            await schemaContext.Database.EnsureCreatedAsync();
            await SeedLedgerAsync(schemaContext, OwnerA, entryCount: 2);
        }

        BackupManifest manifest;
        await using (var backupSourceContext = new DlqDbContext(
            new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(sourceConnectionString).Options))
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["NamespaceRepository:DataDirectory"] = Path.Combine(_tempRoot, "no-namespace-store-here"),
                })
                .Build();
            var backupOptions = Options.Create(new BackupOptions { BackupDirectory = _backupDir, RetentionCount = 14 });
            var protectorMock = new Mock<IConnectionStringProtector>();
            protectorMock.Setup(p => p.GetKeyFingerprint()).Returns(FingerprintValue);

            var backupService = new BackupService(
                backupSourceContext, protectorMock.Object, configuration, backupOptions, NullLogger<BackupService>.Instance);
            var backupResult = await backupService.CreateBackupAsync();
            backupResult.IsSuccess.Should().BeTrue();
            manifest = backupResult.Value;
        }

        var backedUpDbPath = Path.Combine(_backupDir, manifest.BackupId, "servicehub-dlq.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_restoredDbPath)!);
        File.Copy(backedUpDbPath, _restoredDbPath);

        // RecoveryEvent is entirely init-only in EF Core (append-only guard, by design), so
        // simulating tampering means going around EF entirely — a raw UPDATE against the
        // restored SQLite file, exactly the "casual or partial alteration of the underlying
        // SQLite file" the chain-verify doc comment on RecoveryController.VerifyChain describes.
        var restoredConnectionString = $"Data Source={_restoredDbPath}";
        await using (var tamperConnection = new SqliteConnection(restoredConnectionString))
        {
            await tamperConnection.OpenAsync();
            await using var command = tamperConnection.CreateCommand();
            command.CommandText =
                "UPDATE RecoveryEvents SET EventType = @newType " +
                "WHERE Id = (SELECT Id FROM RecoveryEvents WHERE OwnerId = @ownerId ORDER BY Seq LIMIT 1)";
            command.Parameters.AddWithValue("@newType", nameof(RecoveryEventType.ProviderRejected));
            command.Parameters.AddWithValue("@ownerId", OwnerA);
            var rowsAffected = await command.ExecuteNonQueryAsync();
            rowsAffected.Should().Be(1);
        }

        await using var verifyContext = new DlqDbContext(
            new DbContextOptionsBuilder<DlqDbContext>().UseSqlite(restoredConnectionString).Options);
        var restoredLedger = new RecoveryLedgerService(verifyContext);

        var chain = await restoredLedger.VerifyChainAsync(OwnerA);
        chain.IsValid.Should().BeFalse();
    }
}
