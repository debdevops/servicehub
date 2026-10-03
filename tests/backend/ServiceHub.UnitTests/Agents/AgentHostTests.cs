using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Agents;

namespace ServiceHub.UnitTests.Agents;

/// <summary>
/// The agent platform's promises, tested: one host runs everything, a failure is isolated and
/// reported, a pause means "will not act", and a declared authority is a contract.
/// </summary>
public sealed class AgentHostTests
{
    private static AgentDescriptor Descriptor(
        string id,
        AgentAuthority authority = AgentAuthority.Observes,
        AgentKind kind = AgentKind.Watch) =>
        new(id, id, $"Tests the {id} path.", kind, authority, TimeSpan.FromMinutes(1));

    private sealed class StubAgent(AgentDescriptor descriptor, Func<AgentCycleResult> cycle) : IAgent
    {
        public AgentDescriptor Descriptor { get; } = descriptor;
        public int Cycles { get; private set; }

        public Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct)
        {
            Cycles++;
            return Task.FromResult(cycle());
        }
    }

    private static async Task<(AgentRegistry Registry, AgentHost Host)> RunOneCycleAsync(params IAgent[] agents)
    {
        var registry = new AgentRegistry(agents);
        var time = new FakeTimeProvider();
        var host = new AgentHost(agents, registry, NullLogger<AgentHost>.Instance, time);

        using var cts = new CancellationTokenSource();
        await host.StartAsync(cts.Token);

        // Let the first cycle of every agent run, then stop before the cadence elapses.
        await WaitUntilAsync(() => agents.All(a => registry.StateOf(a.Descriptor.Id)?.LastRunUtc is not null));
        await host.StopAsync(CancellationToken.None);

        return (registry, host);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(5);
        }
    }

    [Fact]
    public async Task A_registered_agent_runs_and_reports_its_cycle()
    {
        var agent = new StubAgent(Descriptor("dlq-monitor"), () => new AgentCycleResult(12, 0, "scanned 12 queues"));

        var (registry, _) = await RunOneCycleAsync(agent);

        var state = registry.StateOf("dlq-monitor");
        state.Should().NotBeNull();
        state!.Health.Should().Be(AgentHealth.Healthy);
        state.LastResult!.Summary.Should().Be("scanned 12 queues");
        state.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public async Task A_failing_agent_is_recorded_and_does_not_stop_its_siblings()
    {
        var failing = new StubAgent(Descriptor("failing"), () => throw new InvalidOperationException("provider unreachable"));
        var healthy = new StubAgent(Descriptor("healthy"), () => AgentCycleResult.Idle());

        var (registry, _) = await RunOneCycleAsync(failing, healthy);

        registry.StateOf("failing")!.Health.Should().Be(AgentHealth.Failing);
        registry.StateOf("failing")!.LastFailure.Should().Contain("provider unreachable");
        registry.StateOf("healthy")!.Health.Should().Be(AgentHealth.Healthy,
            "one agent's failure must never take down the host or its siblings");
    }

    [Fact]
    public async Task An_agent_that_reports_a_change_it_has_no_authority_to_make_is_a_contract_violation()
    {
        // Rule R8. An agent that declares it only observes, and then reports a mutation, is a
        // defect to surface — never a statistic to record quietly.
        var agent = new StubAgent(
            Descriptor("over-reaching", AgentAuthority.Observes),
            () => new AgentCycleResult(3, 1, "replayed one"));

        var (registry, _) = await RunOneCycleAsync(agent);

        var state = registry.StateOf("over-reaching")!;
        state.Health.Should().Be(AgentHealth.Failing);
        state.LastFailure.Should().Contain("Authority is a contract");
    }

    [Fact]
    public async Task An_acting_agent_may_report_changes()
    {
        var agent = new StubAgent(
            Descriptor("replay", AgentAuthority.ActsWithApproval, AgentKind.Act),
            () => new AgentCycleResult(3, 1, "replayed one approved message"));

        var (registry, _) = await RunOneCycleAsync(agent);

        registry.StateOf("replay")!.Health.Should().Be(AgentHealth.Healthy);
    }

    [Fact]
    public async Task A_degraded_cycle_is_degraded_not_failing()
    {
        var agent = new StubAgent(
            Descriptor("partial"),
            () => new AgentCycleResult(9, 0, "one entity could not be scanned", Degraded: true));

        var (registry, _) = await RunOneCycleAsync(agent);

        registry.StateOf("partial")!.Health.Should().Be(AgentHealth.Degraded);
    }

    [Fact]
    public void A_paused_agent_is_paused_and_an_unknown_id_is_reported()
    {
        var agent = new StubAgent(Descriptor("pausable"), () => AgentCycleResult.Idle());
        var registry = new AgentRegistry([agent]);

        registry.SetPaused("pausable", true).Should().BeTrue();
        registry.StateOf("pausable")!.IsPaused.Should().BeTrue();
        registry.StateOf("pausable")!.Health.Should().Be(AgentHealth.Paused);

        registry.SetPaused("no-such-agent", true).Should().BeFalse();
    }

    [Fact]
    public void Two_agents_cannot_share_an_id()
    {
        var a = new StubAgent(Descriptor("duplicate"), () => AgentCycleResult.Idle());
        var b = new StubAgent(Descriptor("duplicate"), () => AgentCycleResult.Idle());

        var act = () => new AgentRegistry([a, b]);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*duplicate*", "an agent id is a stable identity — pause state and history key on it");
    }

    [Fact]
    public void A_newly_registered_agent_is_Unknown_not_Healthy()
    {
        var agent = new StubAgent(Descriptor("fresh"), () => AgentCycleResult.Idle());
        var registry = new AgentRegistry([agent]);

        registry.StateOf("fresh")!.Health.Should().Be(AgentHealth.Unknown,
            "claiming health before a cycle has run would be claiming knowledge we do not have");
    }
}
