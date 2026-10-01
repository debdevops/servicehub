using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Agents;

namespace ServiceHub.UnitTests.Agents;

/// <summary>
/// An agent no connected cloud gives anything to do is not run and not listed, and comes back when one is connected —
/// with whatever pause a person set left exactly as it was. Auto Replay and the other always-on agents are never touched.
/// </summary>
public sealed class AgentApplicabilityTests
{
    private sealed class StubAgent(string id, AgentNeeds needs) : IAgent
    {
        public AgentDescriptor Descriptor { get; } = new(id, id, $"Tests {id}.", AgentKind.Watch, AgentAuthority.Observes, TimeSpan.FromMinutes(1), Needs: needs);
        public int Cycles { get; private set; }

        public Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
        {
            Cycles++;
            return Task.FromResult(new AgentCycleResult(1, 0, "ok"));
        }
    }

    private static Namespace Connected(CloudProviderType provider) =>
        Namespace.Create("ns-" + provider, "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=abcdefghijklmnop=", "ns", provider: provider).Value;

    private sealed class Clouds(params CloudProviderType[] connected)
    {
        public CloudProviderType[] Connected { get; set; } = connected;
    }

    private static (AgentHost Host, AgentRegistry Registry, Clouds Clouds) Build(Clouds clouds, params IAgent[] agents)
    {
        var repo = new Mock<INamespaceRepository>();
        repo.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => Result<IReadOnlyList<Namespace>>.Success([.. clouds.Connected.Select(Connected)]));
        var scopes = new ServiceCollection().AddSingleton(repo.Object).BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var registry = new AgentRegistry(agents);
        return (new AgentHost(agents, registry, NullLogger<AgentHost>.Instance, new FakeTimeProvider(), scopes), registry, clouds);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task With_only_AWS_and_Google_the_agents_that_need_a_watched_or_verifiable_cloud_are_off_and_unlisted()
    {
        var monitor = new StubAgent("dlq-monitor", AgentNeeds.WatchedCloud);
        var trust = new StubAgent("autonomy-evaluation", AgentNeeds.VerifiableCloud);
        var autoReplay = new StubAgent("auto-replay", AgentNeeds.None);
        var (host, registry, _) = Build(new Clouds(CloudProviderType.Aws, CloudProviderType.Gcp), monitor, trust, autoReplay);

        await host.ReconcileAsync(CancellationToken.None);
        await WaitUntilAsync(() => autoReplay.Cycles > 0);

        registry.All().Select(a => a.Descriptor.Id).Should().Equal("auto-replay");
        registry.Dormant().Select(d => d.Id).Should().BeEquivalentTo("dlq-monitor", "autonomy-evaluation");
        monitor.Cycles.Should().Be(0);
        trust.Cycles.Should().Be(0);
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task Connecting_Azure_starts_them_and_removing_it_stops_them_again()
    {
        var monitor = new StubAgent("dlq-monitor", AgentNeeds.WatchedCloud);
        var (host, registry, clouds) = Build(new Clouds(CloudProviderType.Aws), monitor);

        await host.ReconcileAsync(CancellationToken.None);
        registry.All().Should().BeEmpty();

        clouds.Connected = [CloudProviderType.Aws, CloudProviderType.Azure];
        await host.ReconcileAsync(CancellationToken.None);
        await WaitUntilAsync(() => monitor.Cycles > 0);
        registry.All().Select(a => a.Descriptor.Id).Should().Equal("dlq-monitor");
        registry.Dormant().Should().BeEmpty();

        clouds.Connected = [CloudProviderType.Aws];
        await host.ReconcileAsync(CancellationToken.None);
        registry.All().Should().BeEmpty();
        registry.Dormant().Select(d => d.Id).Should().Equal("dlq-monitor");
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task A_pause_a_person_set_survives_the_agent_going_off_and_coming_back()
    {
        var trust = new StubAgent("autonomy-evaluation", AgentNeeds.VerifiableCloud);
        var (host, registry, clouds) = Build(new Clouds(CloudProviderType.Azure), trust);
        await host.ReconcileAsync(CancellationToken.None);
        registry.SetPaused("autonomy-evaluation", true);

        clouds.Connected = [CloudProviderType.Aws];
        await host.ReconcileAsync(CancellationToken.None);
        clouds.Connected = [CloudProviderType.Azure];
        await host.ReconcileAsync(CancellationToken.None);

        registry.StateOf("autonomy-evaluation")!.IsPaused.Should().BeTrue();
        await host.StopAsync(CancellationToken.None);
    }

    [Fact]
    public async Task An_agent_that_is_off_is_never_late_so_it_raises_nothing()
    {
        var trust = new StubAgent("autonomy-evaluation", AgentNeeds.VerifiableCloud);
        var (host, registry, clouds) = Build(new Clouds(CloudProviderType.Azure), trust);
        await host.ReconcileAsync(CancellationToken.None);
        await WaitUntilAsync(() => registry.StateOf("autonomy-evaluation")!.LastRunUtc is not null);

        clouds.Connected = [CloudProviderType.Gcp];
        await host.ReconcileAsync(CancellationToken.None);

        registry.StateOf("autonomy-evaluation")!.NeedsAPerson(DateTimeOffset.UtcNow.AddDays(30)).Should().BeFalse();
        await host.StopAsync(CancellationToken.None);
    }

    [Theory]
    [InlineData(AgentNeeds.WatchedCloud, CloudProviderType.Azure, true)]
    [InlineData(AgentNeeds.WatchedCloud, CloudProviderType.Aws, false)]
    [InlineData(AgentNeeds.WatchedCloud, CloudProviderType.Gcp, false)]
    [InlineData(AgentNeeds.VerifiableCloud, CloudProviderType.Azure, true)]
    [InlineData(AgentNeeds.VerifiableCloud, CloudProviderType.Aws, false)]
    [InlineData(AgentNeeds.None, CloudProviderType.Gcp, true)]
    public void Meaningful_follows_the_cloud_capability(AgentNeeds needs, CloudProviderType provider, bool expected) =>
        new StubAgent("x", needs).Descriptor.IsMeaningfulFor([provider]).Should().Be(expected);
}
