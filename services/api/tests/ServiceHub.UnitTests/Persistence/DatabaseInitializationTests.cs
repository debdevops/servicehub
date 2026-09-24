using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Persistence;

/// <summary>
/// Unit 1.1's "done when": a first run creates the file and applies the migration, a second
/// process fails fast, and a database that is not ours is refused rather than adopted (ADR-0015 D4).
/// </summary>
public sealed class DatabaseInitializationTests : IDisposable
{
    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"servicehub-init-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private ServiceProvider BuildProvider(string? dataDirectory = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ServiceHubDataDirectory.ConfigurationKey] = dataDirectory ?? _dataDir,
                // The database registers the connection-string protector (unit 1.2), which by
                // design refuses to start without a key.
                ["Security:EncryptionKey"] = "DEV_KEY_NOT_FOR_PRODUCTION_12345678901234567890"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Development));
        services.AddLogging();
        services.AddServiceHubPersistence();
        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task FirstRun_CreatesTheFileInTheConfiguredDirectory_AndAppliesTheMigration()
    {
        await using var provider = BuildProvider();

        await provider.InitializeServiceHubDatabaseAsync();

        var dbPath = Path.Combine(_dataDir, ServiceHubDataDirectory.DatabaseFileName);
        File.Exists(dbPath).Should().BeTrue("the configured data directory is the one used");
        File.Exists(Path.Combine(_dataDir, ".instance.lock")).Should().BeTrue("the lock lives beside the database");

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        (await db.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).Should().ContainSingle()
            .Which.Should().Be("0001_W1_NamespacesAndAudit");
        (await db.Namespaces.CountAsync()).Should().Be(0);
        (await db.AuditLogs.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task SecondInstance_AgainstTheSameDirectory_FailsFastWithAClearMessage()
    {
        await using var first = BuildProvider();
        await first.InitializeServiceHubDatabaseAsync();

        await using var second = BuildProvider();
        var act = () => second.InitializeServiceHubDatabaseAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("Another ServiceHub instance").And.Contain(_dataDir);
    }

    [Fact]
    public async Task RestartAfterShutdown_ReopensTheSameDatabase()
    {
        await using (var first = BuildProvider())
        {
            await first.InitializeServiceHubDatabaseAsync();
        }

        await using var second = BuildProvider();
        await second.InitializeServiceHubDatabaseAsync();

        await using var scope = second.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        (await db.Database.GetAppliedMigrationsAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ADatabaseWithAnUnknownMigration_IsRefused_NotAdopted()
    {
        Directory.CreateDirectory(_dataDir);
        CreateForeignDatabase(
            "CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" TEXT NOT NULL PRIMARY KEY, \"ProductVersion\" TEXT NOT NULL);" +
            "INSERT INTO \"__EFMigrationsHistory\" VALUES ('20260101000000_Initial', '10.0.0');" +
            "CREATE TABLE \"DlqMessages\" (\"Id\" TEXT);");

        await using var provider = BuildProvider();
        var act = () => provider.InitializeServiceHubDatabaseAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("not a ServiceHub 4.1.0 database").And.Contain("ServiceHub:DataDirectory");
    }

    [Fact]
    public async Task ADatabaseWithTablesButNoMigrationHistory_IsRefused()
    {
        Directory.CreateDirectory(_dataDir);
        CreateForeignDatabase("CREATE TABLE \"Somebody\" (\"Id\" TEXT);");

        await using var provider = BuildProvider();
        var act = () => provider.InitializeServiceHubDatabaseAsync();

        (await act.Should().ThrowAsync<InvalidOperationException>())
            .Which.Message.Should().Contain("not a ServiceHub 4.1.0 database");
    }

    [Fact]
    public void DataDirectory_WhenNothingIsConfigured_IsDataBeneathTheCurrentDirectory()
    {
        var resolved = ServiceHubDataDirectory.Resolve(new ConfigurationBuilder().Build());

        resolved.Should().Be(Path.GetFullPath("data"));
    }

    [Fact]
    public async Task ReadinessCheck_ReportsHealthy_OnceTheDatabaseExists()
    {
        await using var provider = BuildProvider();
        await provider.InitializeServiceHubDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var check = ActivatorUtilities.CreateInstance<SqliteDatabaseHealthCheck>(scope.ServiceProvider);
        var result = await check.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        result.Status.Should().Be(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Healthy);
        result.Data["JournalMode"].Should().Be("wal");
    }

    [Fact]
    public async Task ReadinessCheck_ReportsUnhealthy_WhenTheFileIsMissing()
    {
        await using var provider = BuildProvider();

        await using var scope = provider.CreateAsyncScope();
        var check = ActivatorUtilities.CreateInstance<SqliteDatabaseHealthCheck>(scope.ServiceProvider);
        var result = await check.CheckHealthAsync(new Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext());

        result.Status.Should().Be(Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus.Unhealthy);
    }

    private void CreateForeignDatabase(string sql)
    {
        using var connection = new SqliteConnection($"Data Source={Path.Combine(_dataDir, ServiceHubDataDirectory.DatabaseFileName)}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }
}
