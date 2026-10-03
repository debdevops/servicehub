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
    private readonly bool _keepData;

    /// <summary>A host over a fresh directory, deleted when the host is disposed. The one public constructor (xUnit fixtures allow no more).</summary>
    public ServiceHubApiFactory()
        : this(null, keepData: false)
    {
    }

    private ServiceHubApiFactory(string? dataDirectory, bool keepData)
    {
        DataDirectory = dataDirectory ?? Path.Combine(Path.GetTempPath(), $"servicehub-api-tests-{Guid.NewGuid():N}");
        _keepData = keepData;
    }

    /// <summary>A host over <paramref name="dataDirectory"/> that leaves it in place on dispose — how a test restarts ServiceHub over the same database.</summary>
    public static ServiceHubApiFactory Reusing(string dataDirectory) => new(dataDirectory, keepData: true);

    /// <summary>The directory this host owns.</summary>
    public string DataDirectory { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.UseSetting("ServiceHub:DataDirectory", DataDirectory);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && !_keepData && Directory.Exists(DataDirectory))
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(DataDirectory, recursive: true);
        }
    }
}
