using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.UnitTests.Persistence;

/// <summary>
/// Unit 1.2's "done when": a namespace row shows an <c>ENC[…]</c> envelope in the SQLite file, and
/// the plaintext is recoverable only through the protector.
/// </summary>
public sealed class ConnectionStringEncryptionTests : IDisposable
{
    private const string Plaintext =
        "Endpoint=sb://acme.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey=super-secret-key-value==";

    private const string RegistryJson =
        """{ "ActiveKeyId": "k1", "Keys": [ { "Id": "k1", "Material": "unit-test-key-material-one-32-bytes-min", "Status": "active" } ] }""";

    private readonly string _dataDir = Path.Combine(Path.GetTempPath(), $"servicehub-enc-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_dataDir))
        {
            Directory.Delete(_dataDir, recursive: true);
        }
    }

    private ServiceProvider BuildProvider()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ServiceHubDataDirectory.ConfigurationKey] = _dataDir,
                ["Security:EncryptionKeyRegistry"] = RegistryJson,
                ["Security:EnableConnectionStringEncryption"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton(Mock.Of<IHostEnvironment>(e => e.EnvironmentName == Environments.Development));
        services.AddLogging();
        services.AddServiceHubPersistence();
        return services.BuildServiceProvider();
    }

    private static string ReadRawColumn(string dataDir)
    {
        using var connection = new SqliteConnection(
            $"Data Source={Path.Combine(dataDir, ServiceHubDataDirectory.DatabaseFileName)};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT ConnectionStringEncrypted FROM Namespaces LIMIT 1";
        return (string)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task APlaintextConnectionString_IsStoredAsAnEnvelope_AndNeverInTheFile()
    {
        await using var provider = BuildProvider();
        await provider.InitializeServiceHubDatabaseAsync();

        await using (var scope = provider.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            db.Namespaces.Add(Namespace.Create("acme.servicebus.windows.net", Plaintext, ownerId: "owner1").Value);
            await db.SaveChangesAsync();
        }

        var raw = ReadRawColumn(_dataDir);
        raw.Should().StartWith("ENC[v2:kid=k1");
        raw.Should().NotContain("super-secret-key-value");

        SqliteConnection.ClearAllPools();
        var fileBytes = await File.ReadAllBytesAsync(Path.Combine(_dataDir, ServiceHubDataDirectory.DatabaseFileName));
        System.Text.Encoding.UTF8.GetString(fileBytes).Should().NotContain("super-secret-key-value");
    }

    [Fact]
    public async Task ALoadedNamespace_CarriesTheEnvelope_AndTheProtectorRecoversThePlaintext()
    {
        await using var provider = BuildProvider();
        await provider.InitializeServiceHubDatabaseAsync();

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        db.Namespaces.Add(Namespace.Create("acme.servicebus.windows.net", Plaintext, ownerId: "owner1").Value);
        await db.SaveChangesAsync();

        await using var readScope = provider.CreateAsyncScope();
        var loaded = await readScope.ServiceProvider.GetRequiredService<ServiceHubDbContext>()
            .Namespaces.AsNoTracking().SingleAsync();

        loaded.ConnectionString.Should().StartWith("ENC[v2:kid=k1", "reads never decrypt");
        var protector = provider.GetRequiredService<IConnectionStringProtector>();
        protector.Unprotect(loaded.ConnectionString!).Value.Should().Be(Plaintext);
    }

    [Fact]
    public async Task AnAlreadyProtectedValue_IsNotEncryptedTwice()
    {
        await using var provider = BuildProvider();
        await provider.InitializeServiceHubDatabaseAsync();
        var protector = provider.GetRequiredService<IConnectionStringProtector>();
        var envelope = protector.Protect(Plaintext).Value;

        await using var scope = provider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        db.Namespaces.Add(Namespace.Create("acme.servicebus.windows.net", envelope, ownerId: "owner1").Value);
        await db.SaveChangesAsync();

        ReadRawColumn(_dataDir).Should().Be(envelope);
    }
}
