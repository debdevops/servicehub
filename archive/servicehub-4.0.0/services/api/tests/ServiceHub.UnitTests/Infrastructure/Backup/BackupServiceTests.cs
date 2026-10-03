using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Backup;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Backup;

public sealed class BackupServiceTests : IDisposable
{
    private const string FingerprintValue = "sha256:abc123abc123abcd";

    private readonly string _tempRoot;
    private readonly string _backupDir;
    private readonly string _namespaceDataDir;
    private readonly DlqDbContext _dbContext;
    private readonly Mock<IConnectionStringProtector> _protectorMock = new();

    public BackupServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"servicehub-backup-tests-{Guid.NewGuid():N}");
        _backupDir = Path.Combine(_tempRoot, "backups");
        _namespaceDataDir = Path.Combine(_tempRoot, "namespace-data");
        Directory.CreateDirectory(_namespaceDataDir);

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;

        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _dbContext.DlqMessages.Add(new DlqMessage
        {
            MessageId = "msg-1",
            SequenceNumber = 1,
            BodyHash = "hash-1",
            NamespaceId = Guid.NewGuid(),
            OwnerId = "test-owner",
            EntityName = "test-queue",
            EntityType = ServiceBusEntityType.Queue,
            EnqueuedTimeUtc = DateTimeOffset.UtcNow,
            DetectedAtUtc = DateTimeOffset.UtcNow,
            DeliveryCount = 5,
            MessageSize = 100,
            FailureCategory = FailureCategory.Transient,
            Status = DlqMessageStatus.Active
        });
        _dbContext.SaveChanges();

        _protectorMock.Setup(p => p.GetKeyFingerprint()).Returns(FingerprintValue);
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

    private BackupService CreateService(int retentionCount = 14)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["NamespaceRepository:DataDirectory"] = _namespaceDataDir
            })
            .Build();

        var backupOptions = Options.Create(new BackupOptions
        {
            BackupDirectory = _backupDir,
            RetentionCount = retentionCount
        });

        return new BackupService(
            _dbContext,
            _protectorMock.Object,
            configuration,
            backupOptions,
            NullLogger<BackupService>.Instance);
    }

    // ── CreateBackupAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task CreateBackupAsync_NoNamespaceStore_CreatesSqliteSnapshotWithManifest()
    {
        var service = CreateService();

        var result = await service.CreateBackupAsync();

        result.IsSuccess.Should().BeTrue();
        var manifest = result.Value;

        manifest.IntegrityCheck.Should().Be("ok");
        manifest.Sqlite.FileName.Should().Be("servicehub-dlq.db");
        manifest.Sqlite.SizeBytes.Should().BeGreaterThan(0);
        manifest.Sqlite.Sha256.Should().MatchRegex("^[0-9a-f]{64}$");
        manifest.NamespaceStore.Should().BeNull();
        manifest.EncryptionKeyFingerprint.Should().Be(FingerprintValue);
        manifest.ConsistencyNote.Should().Contain("not").And.Contain("atomic");

        var bundleDir = Path.Combine(_backupDir, manifest.BackupId);
        Directory.Exists(bundleDir).Should().BeTrue();
        File.Exists(Path.Combine(bundleDir, "servicehub-dlq.db")).Should().BeTrue();
        File.Exists(Path.Combine(bundleDir, "manifest.json")).Should().BeTrue();
        File.Exists(Path.Combine(bundleDir, "servicehub-namespaces.json")).Should().BeFalse();
    }

    [Fact]
    public async Task CreateBackupAsync_WithNamespaceStore_CopiesFileAndRecordsChecksum()
    {
        var namespaceStorePath = Path.Combine(_namespaceDataDir, "servicehub-namespaces.json");
        var content = "[{\"id\":\"11111111-1111-1111-1111-111111111111\"}]";
        await File.WriteAllTextAsync(namespaceStorePath, content);

        var service = CreateService();

        var result = await service.CreateBackupAsync();

        result.IsSuccess.Should().BeTrue();
        var manifest = result.Value;

        manifest.NamespaceStore.Should().NotBeNull();
        manifest.NamespaceStore!.FileName.Should().Be("servicehub-namespaces.json");
        manifest.NamespaceStore.SizeBytes.Should().Be(content.Length);

        var copiedPath = Path.Combine(_backupDir, manifest.BackupId, "servicehub-namespaces.json");
        File.Exists(copiedPath).Should().BeTrue();
        (await File.ReadAllTextAsync(copiedPath)).Should().Be(content);
    }

    [Fact]
    public async Task CreateBackupAsync_NeverExposesKeyMaterial_OnlyFingerprint()
    {
        var service = CreateService();

        var result = await service.CreateBackupAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.EncryptionKeyFingerprint.Should().Be(FingerprintValue);
        _protectorMock.Verify(p => p.GetKeyFingerprint(), Times.Once);
        _protectorMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task CreateBackupAsync_CalledTwice_ProducesTwoDistinctBundles()
    {
        var service = CreateService();

        var first = await service.CreateBackupAsync();
        var second = await service.CreateBackupAsync();

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Value.BackupId.Should().NotBe(second.Value.BackupId);

        Directory.GetDirectories(_backupDir).Should().HaveCount(2);
    }

    [Fact]
    public async Task CreateBackupAsync_RetentionExceeded_DeletesOldestBundles()
    {
        var service = CreateService(retentionCount: 2);

        for (var i = 0; i < 4; i++)
        {
            var result = await service.CreateBackupAsync();
            result.IsSuccess.Should().BeTrue();
        }

        Directory.GetDirectories(_backupDir).Should().HaveCount(2);
    }

    // ── ListBackupsAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task ListBackupsAsync_NoBackupsYet_ReturnsEmptyList()
    {
        var service = CreateService();

        var result = await service.ListBackupsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ListBackupsAsync_ReturnsNewestFirst()
    {
        var service = CreateService();

        var first = await service.CreateBackupAsync();
        var second = await service.CreateBackupAsync();
        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();

        var result = await service.ListBackupsAsync();

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value[0].CreatedAtUtc.Should().BeOnOrAfter(result.Value[1].CreatedAtUtc);
        result.Value.Select(s => s.BackupId).Should().Contain([first.Value.BackupId, second.Value.BackupId]);
        result.Value.Should().OnlyContain(s => s.IntegrityCheck == "ok" && !s.NamespaceStorePresent);
    }
}
