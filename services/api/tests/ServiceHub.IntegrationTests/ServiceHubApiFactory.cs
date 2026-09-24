using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Hosts the real application against its own throw-away data directory. Every factory gets a
/// distinct directory because the host now takes a single-instance lock on it (ADR-0003) — two
/// hosts sharing one would, correctly, refuse to start.
/// </summary>
public sealed class ServiceHubApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The directory this host owns.</summary>
    public string DataDirectory { get; } =
        Path.Combine(Path.GetTempPath(), $"servicehub-api-tests-{Guid.NewGuid():N}");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting("ServiceHub:DataDirectory", DataDirectory);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(DataDirectory))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(DataDirectory, recursive: true);
        }
    }
}
