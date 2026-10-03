using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Routing;
using ServiceHub.Providers.Azure;

namespace ServiceHub.IntegrationTests;

internal sealed class DevelopmentEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ApplicationName { get; set; } = "ServiceHub.IntegrationTests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

/// <summary>Skips itself, visibly, unless a real Azure connection string is supplied.</summary>
public sealed class LiveAzureFactAttribute : FactAttribute
{
    public const string Variable = "SERVICEHUB_LIVE_AZURE_CONNECTION_STRING";

    public LiveAzureFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(Variable)))
        {
            Skip = $"Live test: set {Variable} to a Service Bus connection string (a dedicated SAS policy, not RootManageSharedAccessKey).";
        }
    }
}

/// <summary>
/// Unit 1.3's "done when", against a real Azure Service Bus namespace: the router resolves the
/// namespace to the Azure adapter, the connection validates, and real entities come back.
/// </summary>
[Trait("Category", "Live")]
public sealed class AzureLiveConformanceTests
{
    [LiveAzureFact]
    public async Task ARealNamespace_ValidatesAndListsItsEntities_ThroughTheRouter()
    {
        var connectionString = Environment.GetEnvironmentVariable(LiveAzureFactAttribute.Variable)!;
        var dataDir = Path.Combine(Path.GetTempPath(), $"servicehub-live-{Guid.NewGuid():N}");

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ServiceHubDataDirectory.ConfigurationKey] = dataDir,
                ["Security:EncryptionKey"] = "DEV_KEY_NOT_FOR_PRODUCTION_12345678901234567890",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<IHostEnvironment>(new DevelopmentEnvironment());
        services.AddLogging();
        services.AddServiceHubPersistence();
        services.AddCloudProviderRouting();
        services.AddAzureProvider();

        await using var provider = services.BuildServiceProvider();
        try
        {
            await provider.InitializeServiceHubDatabaseAsync();
            await using var scope = provider.CreateAsyncScope();

            var repository = scope.ServiceProvider.GetRequiredService<INamespaceRepository>();
            var host = new Uri(connectionString.Split(';')[0].Replace("Endpoint=sb://", "https://")).Host;
            var ns = Namespace.Create(host, connectionString, ownerId: "live-test").Value;
            (await repository.AddAsync(ns)).IsSuccess.Should().BeTrue();

            var stored = (await repository.GetByIdAsync(ns.Id)).Value;
            stored.ConnectionString.Should().StartWith("ENC[", "the row must never hold the plaintext");

            var azure = scope.ServiceProvider.GetRequiredService<ICloudProviderRouter>().Resolve(stored.Provider);
            azure.Should().BeOfType<AzureMessagingProvider>();

            var validation = await azure.ValidateConnectionAsync(stored, CancellationToken.None);
            validation.IsSuccess.Should().BeTrue(validation.IsFailure ? validation.Error.Message : null);

            var entities = await azure.ListEntitiesAsync(stored.Id, CancellationToken.None);
            entities.IsSuccess.Should().BeTrue(entities.IsFailure ? entities.Error.Message : null);
            entities.Value.Should().NotBeEmpty("a real namespace has queues or topics");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataDir))
            {
                Directory.Delete(dataDir, recursive: true);
            }
        }
    }
}
