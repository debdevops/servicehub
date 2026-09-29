using System.Net;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Unit 1.1 through the real host: the database exists, readiness reflects it, and a second host is refused.</summary>
public sealed class DatabaseStartupTests
{
    [Fact]
    public async Task The_host_creates_the_database_in_the_configured_directory_and_reports_ready()
    {
        using var factory = new ServiceHubApiFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        File.Exists(Path.Combine(factory.DataDirectory, ServiceHubDataDirectory.DatabaseFileName)).Should().BeTrue();
    }

    [Fact]
    public async Task Liveness_does_not_depend_on_the_database()
    {
        using var factory = new ServiceHubApiFactory();
        using var client = factory.CreateClient();
        await client.GetAsync(new Uri("/health", UriKind.Relative));

        // The DLQ monitor runs a cycle as soon as the host starts, and opening a missing SQLite file
        // creates it — so a first cycle landing after the delete below would put the file back and turn
        // this test red under load. Let that first cycle finish, then keep the monitor quiet.
        var registry = factory.Services.GetRequiredService<ServiceHub.Core.Interfaces.IAgentRegistry>();
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (registry.StateOf("dlq-monitor")?.LastRunUtc is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        registry.StateOf("dlq-monitor")?.LastRunUtc.Should().NotBeNull("the monitor's first cycle must be over before the file is removed");
        registry.SetPaused("dlq-monitor", true);

        // Remove the file behind the running host: readiness must notice, liveness must not.
        // (Readiness is Unhealthy -> 503; the process is still alive -> 200.)
        var dbPath = Path.Combine(factory.DataDirectory, ServiceHubDataDirectory.DatabaseFileName);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
        {
            File.Delete(dbPath + suffix);
        }

        (await client.GetAsync(new Uri("/health", UriKind.Relative))).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync(new Uri("/health/ready", UriKind.Relative))).StatusCode
            .Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public void A_second_host_against_the_same_directory_refuses_to_start()
    {
        using var first = new ServiceHubApiFactory();
        _ = first.Services; // starts the host and takes the lock

        using var second = new SharedDirectoryFactory(first.DataDirectory);
        var act = () => second.Services;

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Another ServiceHub instance already holds the data directory*");
    }

    private sealed class SharedDirectoryFactory(string dataDirectory) : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) =>
            builder.UseSetting("ServiceHub:DataDirectory", dataDirectory);
    }
}
