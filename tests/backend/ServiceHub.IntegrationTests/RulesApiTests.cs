using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Unit 3.6: rules through the real host — matched, held by the gate until trust is earned, and stopped by the breaker.</summary>
public sealed class RulesApiTests : IDisposable
{
    public RulesApiTests() => Environment.SetEnvironmentVariable("RecoveryEvidence__CircuitBreakerSampleSize", "4");

    public void Dispose() => Environment.SetEnvironmentVariable("RecoveryEvidence__CircuitBreakerSampleSize", null);

    private static async Task<JsonElement> Send(HttpClient client, string url, object body, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.PostAsJsonAsync(url, body);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static async Task<JsonElement> ListRules(HttpClient client) =>
        JsonDocument.Parse(await client.GetStringAsync("/api/v1/rules?provider=azure")).RootElement;

    private static async Task RunAgent(DeadLettersApiTests.Handle host, string id)
    {
        var agent = host.Services.GetServices<IAgent>().Single(a => a.Descriptor.Id == id);
        await agent.ExecuteCycleAsync(default);
    }

    [Fact]
    public async Task A_missing_cloud_is_refused_never_quietly_read_as_the_first_cloud()
    {
        using var host = DeadLettersApiTests.Host();
        (await host.Client.GetAsync("/api/v1/rules")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.GetAsync("/api/v1/rules/sources")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        await Send(host.Client, "/api/v1/rules", new { name = "No cloud", reason = "Timeout" }, HttpStatusCode.BadRequest);
        await Send(host.Client, "/api/v1/rules/test", new { reason = "Timeout" }, HttpStatusCode.BadRequest);
        (await host.Client.GetAsync("/api/v1/signatures/abc")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.GetAsync("/api/v1/rules?provider=Azure")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Paging_and_range_inputs_out_of_bounds_are_a_400_on_audit_and_signatures_like_every_other_list()
    {
        using var host = DeadLettersApiTests.Host();
        foreach (var bad in new[] { "/api/v1/audit?page=0", "/api/v1/audit?pageSize=0", "/api/v1/audit?pageSize=201",
                                    "/api/v1/signatures?days=0", "/api/v1/signatures?days=31", "/api/v1/signatures?page=0", "/api/v1/signatures?pageSize=101",
                                    "/api/v1/signatures?sort=nope", "/api/v1/signatures/abc?provider=Azure&days=0" })
        {
            (await host.Client.GetAsync(bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest, bad);
        }

        (await host.Client.GetAsync("/api/v1/audit?pageSize=200")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await host.Client.GetAsync("/api/v1/signatures?days=30&pageSize=100&sort=recent")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_rule_needs_a_condition_and_a_sane_pace()
    {
        using var host = DeadLettersApiTests.Host();
        await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "Everything" }, HttpStatusCode.BadRequest);
        await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "x", reason = "Timeout", maxPerHour = 0 }, HttpStatusCode.BadRequest);
        await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "", reason = "Timeout" }, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Testing_a_rule_counts_matches_and_says_why_the_rest_would_be_held_and_sends_nothing()
    {
        var log = new PeekLog { OnReplay = () => ServiceHub.Core.Results.Result<bool>.Success(true) };
        using var host = DeadLettersApiTests.Host(log);
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 5, reason: "Timeout");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 3, reason: "Validation", prefix: "v");

        var test = await Send(host.Client, "/api/v1/rules/test", new { provider = "azure", reason = "Timeout" });

        test.GetProperty("matched").GetInt32().Should().Be(5);
        test.GetProperty("stillWaiting").GetInt32().Should().Be(5);
        test.GetProperty("wouldRun").GetInt32().Should().Be(0, "no failure has earned unattended replay yet");
        test.GetProperty("heldBack").GetInt32().Should().Be(5);
        test.GetProperty("holds")[0].GetProperty("remedy").GetString().Should().NotBeNullOrWhiteSpace();
        log.Replays.Should().Be(0);
    }

    [Fact]
    public async Task A_rule_for_Unknown_picks_up_the_messages_whose_cloud_recorded_no_reason()
    {
        // Regression: Google Cloud often records no dead-letter reason. Every screen calls that "Unknown", so a rule made from one says
        // "Unknown" — but the matcher compared it to NULL and found nothing, so the rule sat there on "0 messages" forever.
        using var host = DeadLettersApiTests.Host(new PeekLog { OnReplay = () => ServiceHub.Core.Results.Result<bool>.Success(true) });
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 4, reason: null);
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 2, reason: "Timeout", prefix: "t");

        var test = await Send(host.Client, "/api/v1/rules/test", new { provider = "azure", reason = "Unknown" });

        test.GetProperty("matched").GetInt32().Should().Be(4, "the four with no recorded reason, not the two that say Timeout");
    }

    [Fact]
    public async Task Messages_already_gone_from_the_queue_are_matched_but_not_held_back()
    {
        using var host = DeadLettersApiTests.Host();
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 3, reason: "Timeout");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 4, reason: "Timeout", status: DlqMessageStatus.Resolved, prefix: "old");

        var test = await Send(host.Client, "/api/v1/rules/test", new { provider = "azure", reason = "Timeout" });

        test.GetProperty("matched").GetInt32().Should().Be(7);
        test.GetProperty("stillWaiting").GetInt32().Should().Be(3);
        test.GetProperty("heldBack").GetInt32().Should().Be(3, "only the three still in the queue can be held; the four already gone have nothing left to decide");
    }

    /// <summary>The test host's own DLQ monitor resolves seeded rows the fake provider does not hold — pause it so a long cycle is not racing it.</summary>
    private static async Task PauseDlqMonitor(HttpClient client)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/agents/dlq-monitor/pause");
        request.Headers.Add("X-ServiceHub-Intent", "pause-agent");
        (await client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    /// <summary>
    /// 2026-09-28: the agent looked only at the oldest 200 matches per cycle, so a rule matching 450 said "200 waiting" (a cap read as a
    /// count) and never reached the newer 250. It now pages through all of them, and says "N+" only past its own bound.
    /// </summary>
    [Fact]
    public async Task A_rule_matching_more_than_one_batch_counts_every_held_message_exactly()
    {
        using var host = DeadLettersApiTests.Host(new PeekLog { OnReplay = () => ServiceHub.Core.Results.Result<bool>.Success(true) });
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await PauseDlqMonitor(host.Client);
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 450, reason: "Timeout");
        await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "Timeouts", reason = "Timeout", waitSeconds = 0 }, HttpStatusCode.Created);

        await RunAgent(host, "auto-replay");

        var rule = (await ListRules(host.Client))[0];
        rule.GetProperty("askedCount").GetInt32().Should().Be(450, "all 450 are held, not just the oldest 200");
        rule.GetProperty("askedIsLowerBound").GetBoolean().Should().BeFalse("450 is below the per-cycle bound, so the count is exact");
    }

    /// <summary>The "waiting for a person" tile summed each rule's own count, so two rules matching the same messages counted them twice.</summary>
    [Fact]
    public async Task Messages_two_rules_both_match_are_waiting_once_not_twice()
    {
        using var host = DeadLettersApiTests.Host(new PeekLog { OnReplay = () => ServiceHub.Core.Results.Result<bool>.Success(true) });
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await PauseDlqMonitor(host.Client);
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 5, reason: "Timeout");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 3, reason: "Validation", prefix: "v");
        await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "By reason", reason = "Timeout", waitSeconds = 0 }, HttpStatusCode.Created);
        await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "By reason and queue", reason = "Timeout", entityName = "orders", waitSeconds = 0 }, HttpStatusCode.Created);
        await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "Other failure", reason = "Validation", waitSeconds = 0 }, HttpStatusCode.Created);

        await RunAgent(host, "auto-replay");

        var rules = await ListRules(host.Client);
        rules.EnumerateArray().Sum(r => r.GetProperty("askedCount").GetInt32()).Should().Be(13, "5 + 5 + 3: each rule counts its own matches");
        var held = JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/rules/held?provider=azure")).RootElement;
        held.GetProperty("distinct").GetInt32().Should().Be(8, "5 Timeout messages (matched by two rules) + 3 Validation messages, each once");
        held.GetProperty("isLowerBound").GetBoolean().Should().BeFalse();
        (await host.Client.GetAsync("/api/v1/rules/held")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a missing cloud is refused, never defaulted");
    }

    [Fact]
    public async Task A_rule_past_the_per_cycle_bound_says_its_count_is_a_floor()
    {
        using var host = DeadLettersApiTests.Host(new PeekLog { OnReplay = () => ServiceHub.Core.Results.Result<bool>.Success(true) });
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await PauseDlqMonitor(host.Client);
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, ServiceHub.Infrastructure.Rules.AutoReplayAgent.MaxLookedAtPerRule + 50, reason: "Timeout");
        await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "Timeouts", reason = "Timeout", waitSeconds = 0 }, HttpStatusCode.Created);

        await RunAgent(host, "auto-replay");

        var rule = (await ListRules(host.Client))[0];
        rule.GetProperty("askedCount").GetInt32().Should().Be(ServiceHub.Infrastructure.Rules.AutoReplayAgent.MaxLookedAtPerRule);
        rule.GetProperty("askedIsLowerBound").GetBoolean().Should().BeTrue("more matches exist than one cycle looks at — show \"N+\"");
    }

    [Fact]
    public async Task A_rule_finds_its_messages_but_the_gate_holds_them_until_trust_is_earned_and_the_rule_says_so()
    {
        var log = new PeekLog { OnReplay = () => ServiceHub.Core.Results.Result<bool>.Success(true) };
        using var host = DeadLettersApiTests.Host(log);
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 4, reason: "Timeout");
        await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "Timeouts", reason = "Timeout", waitSeconds = 0 }, HttpStatusCode.Created);

        await RunAgent(host, "auto-replay");

        log.Replays.Should().Be(0, "automation may only act on a signature that earned an autonomy grant (unit 4.1)");
        var rule = (await ListRules(host.Client))[0];
        rule.GetProperty("askedCount").GetInt32().Should().Be(4);
        rule.GetProperty("lastAskedReason").GetString().Should().StartWith("AUTONOMY");
        rule.GetProperty("replayed").GetInt32().Should().Be(0);
        rule.GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_message_still_inside_its_wait_is_left_alone()
    {
        using var host = DeadLettersApiTests.Host();
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 2, reason: "Timeout");
        await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "Slow", reason = "Timeout", waitSeconds = 3600 }, HttpStatusCode.Created);

        await RunAgent(host, "auto-replay");

        (await ListRules(host.Client))[0].GetProperty("askedCount").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task The_breaker_turns_a_failing_rule_off_says_why_and_never_turns_itself_back_on()
    {
        using var host = DeadLettersApiTests.Host();
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        var created = await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "Bad rule", reason = "Timeout" }, HttpStatusCode.Created);
        var ruleId = created.GetProperty("id").GetInt64();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            var owner = (await db.Namespaces.FindAsync(ns))!.OwnerId;
            // Four verified outcomes, one stayed fixed: 25%, below the 50% floor. A fifth, unverified, must not count.
            var states = new[] { RecoveryEntryState.Recovered, RecoveryEntryState.Returned, RecoveryEntryState.Returned, RecoveryEntryState.Returned, RecoveryEntryState.Unverified };
            for (var i = 0; i < states.Length; i++)
            {
                var entry = new RecoveryLedgerEntry { OperationId = Guid.NewGuid(), OwnerId = owner, BodyHash = "h", TargetEntity = "orders", BegunAt = DateTimeOffset.UtcNow, State = states[i] };
                db.RecoveryLedgerEntries.Add(entry);
                db.ReplayHistories.Add(new ReplayHistory
                {
                    DlqMessageId = i + 1, RuleId = ruleId, RecoveryEntryId = entry.Id, OwnerId = owner, NamespaceId = ns, MessageId = $"m{i}", SourceEntity = "orders",
                    ReplayedAt = DateTimeOffset.UtcNow.AddMinutes(-10 + i), ReplayedBy = "System:AutoReplay", ReplayStrategy = "x", ReplayedToEntity = "orders", OutcomeStatus = "accepted",
                });
            }

            await db.SaveChangesAsync();
        }

        await RunAgent(host, "auto-replay");
        var rule = (await ListRules(host.Client))[0];
        rule.GetProperty("enabled").GetBoolean().Should().BeFalse();
        rule.GetProperty("disabledReason").GetString().Should().Be("CircuitBreaker");
        rule.GetProperty("disabledDetail").GetString().Should().Contain("1 of its last 4").And.Contain("50%");
        rule.GetProperty("verifiedOutcomes").GetInt32().Should().Be(4);

        await RunAgent(host, "auto-replay");
        (await ListRules(host.Client))[0].GetProperty("enabled").GetBoolean().Should().BeFalse("a tripped breaker never resets itself");
    }

    [Fact]
    public async Task A_healthy_rule_is_left_on()
    {
        using var host = DeadLettersApiTests.Host();
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        var ruleId = (await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "Good", reason = "Timeout" }, HttpStatusCode.Created)).GetProperty("id").GetInt64();
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            var owner = (await db.Namespaces.FindAsync(ns))!.OwnerId;
            for (var i = 0; i < 4; i++)
            {
                var entry = new RecoveryLedgerEntry { OperationId = Guid.NewGuid(), OwnerId = owner, BodyHash = "h", TargetEntity = "orders", BegunAt = DateTimeOffset.UtcNow, State = i == 0 ? RecoveryEntryState.Returned : RecoveryEntryState.Recovered };
                db.RecoveryLedgerEntries.Add(entry);
                db.ReplayHistories.Add(new ReplayHistory { DlqMessageId = i + 1, RuleId = ruleId, RecoveryEntryId = entry.Id, OwnerId = owner, NamespaceId = ns, MessageId = $"m{i}", SourceEntity = "orders", ReplayedAt = DateTimeOffset.UtcNow.AddMinutes(-i), ReplayedBy = "System:AutoReplay", ReplayStrategy = "x", ReplayedToEntity = "orders", OutcomeStatus = "accepted" });
            }

            await db.SaveChangesAsync();
        }

        await RunAgent(host, "auto-replay");

        (await ListRules(host.Client))[0].GetProperty("enabled").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task A_person_can_turn_a_rule_off_and_on_and_another_owners_rule_is_not_found()
    {
        using var host = DeadLettersApiTests.Host();
        var id = (await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "R", reason = "Timeout" }, HttpStatusCode.Created)).GetProperty("id").GetInt64();

        var off = await Send(host.Client, $"/api/v1/rules/{id}/enabled", new { enabled = false });
        off.GetProperty("disabledReason").GetString().Should().Be("Person");
        (await Send(host.Client, $"/api/v1/rules/{id}/enabled", new { enabled = true })).GetProperty("enabled").GetBoolean().Should().BeTrue();
        await Send(host.Client, "/api/v1/rules/9999/enabled", new { enabled = true }, HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_rule_can_be_renamed_and_re_paced_but_what_it_matches_never_changes()
    {
        using var host = DeadLettersApiTests.Host();
        var made = await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "Timeouts", reason = "Timeout", entityName = "orders" }, HttpStatusCode.Created);
        var id = made.GetProperty("id").GetInt64();

        var response = await host.Client.PutAsJsonAsync($"/api/v1/rules/{id}", new { name = "Order timeouts", maxPerHour = 3, waitSeconds = 600, backOff = false });
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rule = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        rule.GetProperty("name").GetString().Should().Be("Order timeouts");
        rule.GetProperty("maxPerHour").GetInt32().Should().Be(3);
        rule.GetProperty("waitSeconds").GetInt32().Should().Be(600);
        rule.GetProperty("backOff").GetBoolean().Should().BeFalse();
        rule.GetProperty("reason").GetString().Should().Be("Timeout");
        rule.GetProperty("entityName").GetString().Should().Be("orders");

        (await host.Client.PutAsJsonAsync($"/api/v1/rules/{id}", new { name = "", maxPerHour = 3 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.PutAsJsonAsync($"/api/v1/rules/{id}", new { name = "x", maxPerHour = 0 })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.PutAsJsonAsync("/api/v1/rules/999", new { name = "x" })).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_a_rule_removes_only_the_rule()
    {
        using var host = DeadLettersApiTests.Host();
        var made = await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "Timeouts", reason = "Timeout" }, HttpStatusCode.Created);
        var id = made.GetProperty("id").GetInt64();

        (await host.Client.DeleteAsync($"/api/v1/rules/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await ListRules(host.Client)).GetArrayLength().Should().Be(0);
        (await host.Client.DeleteAsync($"/api/v1/rules/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_rule_lists_the_messages_it_matches_that_are_still_waiting_and_sends_nothing()
    {
        var log = new PeekLog { OnReplay = () => ServiceHub.Core.Results.Result<bool>.Success(true) };
        using var host = DeadLettersApiTests.Host(log);
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 3, reason: "Timeout");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 2, reason: "Timeout", status: DlqMessageStatus.Resolved, prefix: "old");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 4, reason: "Validation", prefix: "v");
        var made = await Send(host.Client, "/api/v1/rules", new { provider = "azure", name = "Timeouts", reason = "Timeout" }, HttpStatusCode.Created);

        var ids = JsonDocument.Parse(await host.Client.GetStringAsync($"/api/v1/rules/{made.GetProperty("id").GetInt64()}/matches")).RootElement;

        ids.GetArrayLength().Should().Be(3, "only the Timeout messages still in the queue");
        (await host.Client.GetAsync("/api/v1/rules/999/matches")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        log.Replays.Should().Be(0);
    }

    [Fact]
    public async Task Generating_rules_needs_a_cloud_and_makes_none_when_no_failure_has_been_seen()
    {
        using var host = DeadLettersApiTests.Host();
        await Send(host.Client, "/api/v1/rules/generate", new { }, HttpStatusCode.BadRequest);
        var made = await Send(host.Client, "/api/v1/rules/generate", new { provider = "azure" });
        made.GetArrayLength().Should().Be(0);
    }
}
