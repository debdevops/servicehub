using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Infrastructure.Persistence;

/// <summary>
/// Exercises <see cref="NamespaceStoreImporter"/> — the one-shot JSON→SQLite cutover (M2) — against
/// a real temp directory and a real (in-memory-SQLite) <see cref="DlqDbContext"/>, covering the
/// backward-compatibility scenarios the persistence design doc §16 calls for: multiple namespaces,
/// shared-owner rows, a malformed entry tripping the fail-closed gate, and idempotency (a second
/// run never double-imports).
/// </summary>
public sealed class NamespaceStoreImporterTests : IDisposable
{
    private readonly string _dataDir;
    private readonly DlqDbContext _dbContext;

    public NamespaceStoreImporterTests()
    {
        _dataDir = Path.Combine(Path.GetTempPath(), "servicehub-import-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dataDir);

        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private IConfiguration BuildConfiguration() =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["NamespaceRepository:DataDirectory"] = _dataDir })
            .Build();

    private string StorageFilePath => Path.Combine(_dataDir, "servicehub-namespaces.json");

    // Enum fields are plain integers, not strings — NamespaceJsonSnapshot.JsonOptions (mirroring
    // InMemoryNamespaceRepository's own JsonOptions) registers no JsonStringEnumConverter, so this
    // must match the real on-disk format exactly. ConnectionAuthType.ConnectionString=0,
    // AwsAccessKey=10; CloudProviderType.Azure=0, Aws=1; EnvironmentType.Dev=0, Prod=2.
    private const string TwoNamespacesWithOneSharedOwnerJson = """
    [
      {
        "id": "11111111-1111-1111-1111-111111111111",
        "name": "azure-ns",
        "displayName": "Azure NS",
        "connectionString": "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=dGVzdGtleQ==",
        "authType": 0,
        "isActive": true,
        "createdAt": "2026-01-01T00:00:00Z",
        "environment": 0,
        "ownerId": "owner-1",
        "sharedWithOwnerIds": ["owner-2"],
        "provider": 0
      },
      {
        "id": "22222222-2222-2222-2222-222222222222",
        "name": "aws-ns",
        "displayName": "AWS NS",
        "connectionString": "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
        "authType": 10,
        "isActive": true,
        "createdAt": "2026-01-01T00:00:00Z",
        "environment": 2,
        "ownerId": "owner-1",
        "provider": 1,
        "awsRegion": "us-east-1"
      }
    ]
    """;

    [Fact]
    public async Task ImportIfPresentAsync_NoFile_SkipsWithoutError()
    {
        await NamespaceStoreImporter.ImportIfPresentAsync(_dbContext, BuildConfiguration(), NullLogger.Instance);

        (await _dbContext.Namespaces.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task ImportIfPresentAsync_ValidFile_ImportsNamespacesAndSharedOwners_ThenRenamesSourceFile()
    {
        await File.WriteAllTextAsync(StorageFilePath, TwoNamespacesWithOneSharedOwnerJson);

        await NamespaceStoreImporter.ImportIfPresentAsync(_dbContext, BuildConfiguration(), NullLogger.Instance);

        var namespaces = await _dbContext.Namespaces.OrderBy(n => n.Name).ToListAsync();
        namespaces.Should().HaveCount(2);
        namespaces[0].Name.Should().Be("aws-ns");
        namespaces[1].Name.Should().Be("azure-ns");

        var shares = await _dbContext.NamespaceSharedOwners.ToListAsync();
        shares.Should().ContainSingle(s => s.OwnerId == "owner-2");

        File.Exists(StorageFilePath).Should().BeFalse();
        File.Exists(StorageFilePath + ".migrated").Should().BeTrue();
    }

    [Fact]
    public async Task ImportIfPresentAsync_NamespacesAlreadyPopulated_SkipsEntirely_EvenIfFileStillExists()
    {
        // Simulates a second startup after a prior successful import that, for whatever reason,
        // left the source file in place — must never double-import.
        var existing = ServiceHub.Core.Entities.Namespace.Create(
            "already-there",
            "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=dGVzdGtleQ==",
            ownerId: "owner-1").Value;
        _dbContext.Namespaces.Add(existing);
        await _dbContext.SaveChangesAsync();

        await File.WriteAllTextAsync(StorageFilePath, TwoNamespacesWithOneSharedOwnerJson);

        await NamespaceStoreImporter.ImportIfPresentAsync(_dbContext, BuildConfiguration(), NullLogger.Instance);

        (await _dbContext.Namespaces.CountAsync()).Should().Be(1);
        File.Exists(StorageFilePath).Should().BeTrue("the guard must skip before ever touching the source file");
    }

    [Fact]
    public async Task ImportIfPresentAsync_OneEntryFailsValidation_AbortsWholeImport_LeavesSourceFileUntouched()
    {
        const string oneValidOneInvalid = """
        [
          {
            "id": "11111111-1111-1111-1111-111111111111",
            "name": "azure-ns",
            "connectionString": "Endpoint=sb://test.servicebus.windows.net/;SharedAccessKeyName=servicehub;SharedAccessKey=dGVzdGtleQ==",
            "authType": 0,
            "isActive": true,
            "createdAt": "2026-01-01T00:00:00Z",
            "environment": 0,
            "ownerId": "owner-1",
            "provider": 0
          },
          {
            "id": "33333333-3333-3333-3333-333333333333",
            "name": "",
            "connectionString": "not-a-valid-connection-string",
            "authType": 0,
            "isActive": true,
            "createdAt": "2026-01-01T00:00:00Z",
            "environment": 0,
            "ownerId": "owner-1",
            "provider": 0
          }
        ]
        """;
        await File.WriteAllTextAsync(StorageFilePath, oneValidOneInvalid);

        var act = () => NamespaceStoreImporter.ImportIfPresentAsync(_dbContext, BuildConfiguration(), NullLogger.Instance);

        await act.Should().ThrowAsync<InvalidOperationException>();

        (await _dbContext.Namespaces.CountAsync()).Should().Be(0, "a failed import must not partially commit");
        File.Exists(StorageFilePath).Should().BeTrue("the source file must be left in place for the operator to fix by hand");
        File.Exists(StorageFilePath + ".migrated").Should().BeFalse();
    }

    [Fact]
    public async Task ImportIfPresentAsync_EmptyArray_ImportsNothing_ButStillRenamesSourceFile()
    {
        await File.WriteAllTextAsync(StorageFilePath, "[]");

        await NamespaceStoreImporter.ImportIfPresentAsync(_dbContext, BuildConfiguration(), NullLogger.Instance);

        (await _dbContext.Namespaces.CountAsync()).Should().Be(0);
        File.Exists(StorageFilePath + ".migrated").Should().BeTrue();
    }
}
