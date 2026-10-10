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
        // (With no cloud connected the host leaves the monitor off, which is just as quiet: wait for it to decide either way.)
        // Every agent, not only the monitor: each one that runs opens the database on its first cycle, and any of those landing
        // after the delete would put the file back. (Found when a tenth agent was registered and shifted the start-up timing.)
        var everyAgent = factory.Services.GetServices<ServiceHub.Core.Interfaces.IAgent>().Select(a => a.Descriptor.Id).ToList();
        bool Settled() => everyAgent.All(id => registry.StateOf(id)?.LastRunUtc is not null || registry.Dormant().Any(d => d.Id == id));
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (!Settled() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        Settled().Should().BeTrue("every agent's first cycle (or the host's decision to leave it off) must be over before the file is removed");
        everyAgent.ForEach(id => registry.SetPaused(id, true));

        // Remove the file behind the running host: readiness must notice, liveness must not.
        // (Readiness is Unhealthy -> 503; the process is still alive -> 200.)
        var dbPath = Path.Combine(factory.DataDirectory, ServiceHubDataDirectory.DatabaseFileName);
        ServiceHubApiFactory.ClearPoolFor(factory.DataDirectory);
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
