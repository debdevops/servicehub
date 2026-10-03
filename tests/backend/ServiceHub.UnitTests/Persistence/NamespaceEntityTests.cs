using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Persistence;

public sealed class NamespaceEntityTests : IDisposable
{
    private const string AzureConnectionString = "Endpoint=sb://acme.servicebus.windows.net/;SharedAccessKeyName=x;SharedAccessKey=abc";
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"servicehub-ns-tests-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(_dbPath + suffix);
        }
    }

    [Fact]
    public void Create_RejectsAnInvalidAzureConnectionString()
    {
        var result = Namespace.Create("acme", "not-a-connection-string");

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().Contain(e => e.Code == ErrorCodes.Namespace.ConnectionStringInvalid);
    }

    [Fact]
    public void Create_LowerCasesTheNameAndDefaultsTheOwner()
    {
        var ns = Namespace.Create("ACME", AzureConnectionString).Value;

        ns.Name.Should().Be("acme");
        ns.OwnerId.Should().Be(Namespace.SpaOwnerId);
        ns.AuthType.Should().Be(ConnectionAuthType.ConnectionString);
    }

    [Fact]
    public void CreateWithManagedIdentity_RefusesAConnectionStringAuthType()
    {
        Namespace.CreateWithManagedIdentity("acme", ConnectionAuthType.ConnectionString).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task ANamespace_RoundTripsThroughSqlite_AndItsConnectionStringLivesInTheEncryptedColumn()
    {
        var options = new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite($"Data Source={_dbPath}").Options;

        await using (var db = new ServiceHubDbContext(options))
        {
            await db.Database.MigrateAsync();
            db.Namespaces.Add(Namespace.Create("acme", "ENC[v2:kid=k1]:ciphertext", provider: CloudProviderType.Azure).Value);
            await db.SaveChangesAsync();

            var columns = await ColumnNamesAsync(db, "Namespaces");
            columns.Should().Contain("ConnectionStringEncrypted").And.NotContain("ConnectionString");
        }

        await using var read = new ServiceHubDbContext(options);
        var stored = await read.Namespaces.SingleAsync();
        stored.Name.Should().Be("acme");
        stored.Provider.Should().Be(CloudProviderType.Azure);
    }

    [Fact]
    public async Task TheSameNameForTheSameOwner_IsRejectedByTheUniqueIndex()
    {
        var options = new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite($"Data Source={_dbPath}").Options;
        await using var db = new ServiceHubDbContext(options);
        await db.Database.MigrateAsync();
        db.Namespaces.Add(Namespace.Create("acme", AzureConnectionString).Value);
        await db.SaveChangesAsync();

        db.Namespaces.Add(Namespace.Create("acme", AzureConnectionString).Value);
        var act = () => db.SaveChangesAsync();

        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private static async Task<List<string>> ColumnNamesAsync(ServiceHubDbContext db, string table)
    {
        var names = new List<string>();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = $"PRAGMA table_info('{table}');";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            names.Add(reader.GetString(1));
        }

        return names;
    }
}
