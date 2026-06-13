using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ServiceHub.Simulator;
using ServiceHub.Simulator.Store;

namespace ServiceHub.IntegrationTests.Infrastructure;

/// <summary>
/// Web application factory that runs the API in Simulator mode.
/// All cloud providers are replaced by in-memory fakes; no real Azure/AWS/GCP credentials needed.
/// </summary>
public sealed class SimulatorWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _testDataDir =
        Path.Combine(Path.GetTempPath(), $"servicehub-simulator-test-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Simulator");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:EncryptionKey"] = "simulator-test-key-for-integration-tests-min-32bytes!",
                ["Security:EnableConnectionStringEncryption"] = "false",
                ["Security:SpaToken:Enabled"] = "false",
                ["Security:Authentication:Enabled"] = "false",
                ["Cors:AllowedOrigins:0"] = "*",
                ["NamespaceRepository:DataDirectory"] = _testDataDir,
                ["RateLimit:MaxRequests"] = "9999",
            });
        });

        builder.ConfigureServices(_ =>
        {
            // No overrides needed — simulator DI registration handles everything
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.UseContentRoot(Directory.GetCurrentDirectory());
        return base.CreateHost(builder);
    }

    /// <summary>
    /// Resets the simulator state and re-seeds with the default dataset.
    /// Call this in test BeforeEach to guarantee a clean slate.
    /// </summary>
    public void ResetSimulator()
    {
        using var scope = Services.CreateScope();
        var seeder = scope.ServiceProvider.GetRequiredService<SimulatorDataSeeder>();
        seeder.Seed();
        var clock = scope.ServiceProvider.GetRequiredService<SimulatorClock>();
        clock.Reset();
        var store = scope.ServiceProvider.GetRequiredService<ISimulatorStore>();
        store.ClearFaults();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing && Directory.Exists(_testDataDir))
        {
            try { Directory.Delete(_testDataDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }
}
