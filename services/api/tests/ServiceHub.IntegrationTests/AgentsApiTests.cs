using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Agents;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Units 4.3–4.4 through the real host: the API lists exactly the agents that run, each one's own words and limits,
/// and the one control — pause — which is audited and survives a restart.
/// </summary>
public sealed class AgentsApiTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Post(HttpClient client, string url, string? intent)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url);
        if (intent is not null)
        {
            request.Headers.Add("X-ServiceHub-Intent", intent);
        }

        return client.SendAsync(request);
    }

    [Fact]
    public async Task It_lists_exactly_the_agents_that_run_and_says_which_can_change_anything()
    {
        using var host = new ServiceHubApiFactory();
        using var client = host.CreateClient();

        var agents = (await Json(await client.GetAsync("/api/v1/agents"))).EnumerateArray().ToList();

        agents.Select(a => a.GetProperty("id").GetString()).Should().BeEquivalentTo(
            ["dlq-monitor", "recovery-verification", "bulk-replay", "auto-replay", "autonomy-evaluation",
             "insights-anomaly", "insights-backlog", "insights-correlation", "insights-narration"],
            "the screen can only show what the registry holds — never 4.0.0's twenty workers (the four Insights agents arrived with 6.18; the backup agent only when scheduled)");
        agents.Where(a => a.GetProperty("canAct").GetBoolean()).Select(a => a.GetProperty("id").GetString())
            .Should().BeEquivalentTo(["bulk-replay", "auto-replay"], "only two agents can change anything outside ServiceHub");
        foreach (var a in agents)
        {
            a.GetProperty("purpose").GetString().Should().NotBeNullOrWhiteSpace();
            a.GetProperty("mayNot").GetArrayLength().Should().BeGreaterThan(0, $"{a.GetProperty("id")} must say its limits");
        }

        (await client.GetAsync("/api/v1/agents/no-such-agent")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Pause_needs_its_intent_is_audited_and_shows_on_the_agents_timeline()
    {
        using var host = new ServiceHubApiFactory();
        using var client = host.CreateClient();

        (await Post(client, "/api/v1/agents/auto-replay/pause", null)).StatusCode.Should().Be((HttpStatusCode)428);
        (await Json(await client.GetAsync("/api/v1/agents/auto-replay"))).GetProperty("isPaused").GetBoolean().Should().BeFalse();

        var paused = await Json(await Post(client, "/api/v1/agents/auto-replay/pause", "pause-agent"));
        paused.GetProperty("isPaused").GetBoolean().Should().BeTrue();
        paused.GetProperty("health").GetString().Should().Be("paused");

        var timeline = await Json(await client.GetAsync("/api/v1/agents/auto-replay/activity"));
        timeline.GetProperty("items").EnumerateArray().Should().Contain(i => i.GetProperty("kind").GetString() == "paused");
        (await Json(await client.GetAsync("/api/v1/audit?action=Agent.Pause"))).GetProperty("items").GetArrayLength().Should().Be(1);

        (await Post(client, "/api/v1/agents/auto-replay/resume", "pause-agent")).StatusCode.Should().Be((HttpStatusCode)428, "resuming gives authority back, so it must be meant too");
        (await Json(await Post(client, "/api/v1/agents/auto-replay/resume", "resume-agent"))).GetProperty("isPaused").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_pause_is_still_in_force_after_a_restart()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"servicehub-api-tests-{Guid.NewGuid():N}");
        try
        {
            using (var first = ServiceHubApiFactory.Reusing(directory))
            using (var client = first.CreateClient())
            {
                (await Post(client, "/api/v1/agents/auto-replay/pause", "pause-agent")).StatusCode.Should().Be(HttpStatusCode.OK);
            }

            using var second = ServiceHubApiFactory.Reusing(directory);
            using var again = second.CreateClient();
            (await Json(await again.GetAsync("/api/v1/agents/auto-replay"))).GetProperty("isPaused").GetBoolean()
                .Should().BeTrue("a pause that lifted itself on restart would let an acting agent act with nobody having said so");
            (await Json(await again.GetAsync("/api/v1/agents/dlq-monitor"))).GetProperty("isPaused").GetBoolean().Should().BeFalse();
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    /// <summary>Gate 4's extensibility test: one class plus one AddAgent line, and it is on the API with no other change.</summary>
    private sealed class ThrowawayAgent : IAgent
    {
        public AgentDescriptor Descriptor { get; } = new(
            "throwaway", "Throwaway", "Exists only to prove a new agent needs one file and one line.",
            AgentKind.Maintain, AgentAuthority.Observes, TimeSpan.FromMinutes(5), MayNot: ["Anything at all"]);

        public Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct) => Task.FromResult(AgentCycleResult.Idle());
    }

    [Fact]
    public async Task A_new_agent_needs_one_class_and_one_registration_line_and_nothing_else()
    {
        using var root = new ServiceHubApiFactory();
        using var host = root.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddAgent<ThrowawayAgent>()));
        using var client = host.CreateClient();

        var agent = await Json(await client.GetAsync("/api/v1/agents/throwaway"));
        agent.GetProperty("purpose").GetString().Should().StartWith("Exists only");
        agent.GetProperty("kind").GetString().Should().Be("maintain");
        (await Post(client, "/api/v1/agents/throwaway/pause", "pause-agent")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
