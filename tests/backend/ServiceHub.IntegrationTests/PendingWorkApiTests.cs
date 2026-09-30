using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Units 5.1, 5.2 and 5.10 through the real host: a rule that may not act on its own leaves durable pending work (once per dead
/// letter), the count honours the allow-list, and a person's yes or no resolves it — yes through the one gated replay route.
/// </summary>
public sealed class PendingWorkApiTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private static Task<HttpResponseMessage> Post(HttpClient client, string url, string? intent, object? body = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (intent is not null) request.Headers.Add("X-ServiceHub-Intent", intent);
        return client.SendAsync(request);
    }

    /// <summary>An AWS namespace with <paramref name="count"/> dead letters and an enabled rule that matches them.</summary>
    private static async Task<(DeadLettersApiTests.Handle Host, PeekLog Log, Guid Ns)> Held(int count = 2)
    {
        var aws = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        var host = DeadLettersApiTests.Host(new PeekLog(), aws);
        // The fake cloud's queue is empty, so a DLQ monitor cycle on a loaded machine would mark the seeded messages gone.
        host.Services.GetRequiredService<IAgentRegistry>().SetPaused("dlq-monitor", true);
        var ns = await DeadLettersApiTests.Connect(host.Client, "aws");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Aws, count, reason: "Timeout");
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        var owner = (await db.Namespaces.FindAsync(ns))!.OwnerId;
        foreach (var m in db.DlqMessages) m.SignatureHash = "sig-timeout";
        db.AutoReplayRules.Add(new AutoReplayRule
        {
            OwnerId = owner, Name = "Retry timeouts", Provider = CloudProviderType.Aws, Reason = "Timeout", WaitSeconds = 0, BackOff = false,
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await db.SaveChangesAsync();
        return (host, aws, ns);
    }

    private static Task RunAutoReplay(DeadLettersApiTests.Handle host) =>
        host.Services.GetServices<IAgent>().Single(a => a.Descriptor.Id == "auto-replay").ExecuteCycleAsync(default);

    [Fact]
    public async Task Nothing_is_waiting_until_an_agent_stops_and_asks()
    {
        using var host = DeadLettersApiTests.Host();
        var count = await Json(await host.Client.GetAsync("/api/v1/pending-work/count"));
        count.GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task A_held_automatic_replay_is_pending_work_with_the_gates_reason_code_and_is_recorded_once_not_per_cycle()
    {
        var (host, log, _) = await Held(2);
        using var _h = host;
        await RunAutoReplay(host);
        await RunAutoReplay(host); // the agent looks again next cycle — that must not ask again

        log.Replays.Should().Be(0, "AWS cannot prove a fix held, so nothing replays on its own");
        var page = await Json(await host.Client.GetAsync("/api/v1/pending-work"));
        page.GetProperty("total").GetInt32().Should().Be(2);
        var item = page.GetProperty("items")[0];
        item.GetProperty("kind").GetString().Should().Be("approval");
        item.GetProperty("reasonCode").GetString().Should().NotBeNullOrEmpty().And.NotBe("UNKNOWN");
        item.GetProperty("reason").GetString().Should().NotBeNullOrEmpty();
        item.GetProperty("ruleName").GetString().Should().Be("Retry timeouts");
        page.GetProperty("byProvider")[0].GetProperty("provider").GetString().Should().Be("aws");

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        (await db.RecoveryLedgerEntries.CountAsync(e => e.State == RecoveryEntryState.Declined)).Should().Be(2, "one per dead letter, never one per retry");
        var owner = (await db.Namespaces.FirstAsync()).OwnerId;
        (await scope.ServiceProvider.GetRequiredService<IRecoveryLedger>().VerifyChainAsync(owner)).IsValid.Should().BeTrue("the escalations are hash-chained evidence");

        var awsOnly = await Json(await host.Client.GetAsync("/api/v1/pending-work/count?provider=Azure"));
        awsOnly.GetProperty("total").GetInt32().Should().Be(0, "Home is one cloud: Azure's bell-scope shows none of AWS's questions");
    }

    [Fact]
    public async Task A_namespace_outside_the_callers_allow_list_is_not_counted()
    {
        var (host, _, ns) = await Held(1);
        using var _h = host;
        await RunAutoReplay(host);

        var service = host.Services.CreateScope().ServiceProvider.GetRequiredService<IPendingWorkService>();
        using var scope = host.Services.CreateScope();
        var owner = (await scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>().Namespaces.FindAsync(ns))!.OwnerId;

        (await service.ListAsync(new PendingWorkScope(owner, null), 10, default)).Total.Should().Be(1);
        (await service.ListAsync(new PendingWorkScope(owner, new HashSet<Guid> { Guid.NewGuid() }), 10, default)).Total
            .Should().Be(0, "a restricted key must not count namespaces it cannot see");
    }

    [Fact]
    public async Task A_question_beyond_the_lists_cap_can_still_be_answered()
    {
        // Live 2026-09-29: with 603 waiting, the list shows 500, and approving any entry past the 500th was a 404
        // ("nothing is waiting") because the lookup scanned that capped list.
        var (host, log, _) = await Held(505);
        using var _h = host;
        await RunAutoReplay(host);

        var page = await Json(await host.Client.GetAsync("/api/v1/pending-work?limit=500"));
        page.GetProperty("total").GetInt32().Should().Be(505);
        page.GetProperty("items").GetArrayLength().Should().Be(500);
        var shown = page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("entryId").GetGuid()).ToHashSet();

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        var hidden = await db.RecoveryLedgerEntries.Where(e => e.State == RecoveryEntryState.Declined).Select(e => e.Id).ToListAsync();
        var beyond = hidden.First(id => !shown.Contains(id));

        var approved = await Post(host.Client, $"/api/v1/pending-work/{beyond}/approve", "approve-escalation");
        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        log.Replays.Should().Be(1);
    }

    [Fact]
    public async Task The_ledger_can_be_narrowed_by_who_acted_and_shows_the_level_the_signature_held_at_the_time()
    {
        var (host, _, _) = await Held(2);
        using var _h = host;
        await RunAutoReplay(host); // two Declined entries by the rule, while the signature is at the floor

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            var owner = (await db.Namespaces.FirstAsync()).OwnerId;
            (await scope.ServiceProvider.GetRequiredService<IRecoveryLedger>().RecordAutonomyGrantTransitionAsync(
                owner, "sig-timeout", RecoveryOperationKind.Replay, AutonomyLevel.Approve, AutonomyLevel.Standing, "test promotion", null)).IsSuccess.Should().BeTrue();
        }

        var waiting = (await Json(await host.Client.GetAsync("/api/v1/pending-work"))).GetProperty("items")[0].GetProperty("entryId").GetGuid();
        (await Post(host.Client, $"/api/v1/pending-work/{waiting}/approve", "approve-escalation")).StatusCode.Should().Be(HttpStatusCode.OK);

        var all = await Json(await host.Client.GetAsync("/api/v1/recovery/entries?window=24h&pageSize=50"));
        var autonomous = await Json(await host.Client.GetAsync("/api/v1/recovery/entries?window=24h&by=autonomous"));
        var people = await Json(await host.Client.GetAsync("/api/v1/recovery/entries?window=24h&by=people"));
        autonomous.GetProperty("total").GetInt32().Should().Be(2, "the rule declined two");
        people.GetProperty("total").GetInt32().Should().Be(1, "one person approved one");
        people.GetProperty("items")[0].GetProperty("level").GetString().Should().Be("standing", "it began after the promotion");
        autonomous.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("level").GetString()).Should().OnlyContain(l => l == "approve", "they began before it");
        all.GetProperty("total").GetInt32().Should().Be(3);

        (await Json(await host.Client.GetAsync("/api/v1/recovery/entries?window=24h&q=AutoReplay"))).GetProperty("total").GetInt32().Should().Be(2, "search finds who did it");
        (await Json(await host.Client.GetAsync("/api/v1/recovery/entries?window=24h&q=no-such-thing"))).GetProperty("total").GetInt32().Should().Be(0);
        (await host.Client.GetAsync("/api/v1/recovery/entries?by=everyone")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_replayed_list_can_be_narrowed_by_how_it_ended_who_made_it_the_queue_and_a_word()
    {
        var (host, _, _) = await Held(1);
        using var _h = host;
        await RunAutoReplay(host);
        var entry = (await Json(await host.Client.GetAsync("/api/v1/pending-work"))).GetProperty("items")[0].GetProperty("entryId").GetGuid();
        (await Post(host.Client, $"/api/v1/pending-work/{entry}/approve", "approve-escalation")).StatusCode.Should().Be(HttpStatusCode.OK);

        async Task<int> Total(string q) => (await Json(await host.Client.GetAsync($"/api/v1/replays?provider=Aws&{q}"))).GetProperty("total").GetInt32();
        (await Total("")).Should().Be(1);
        (await Total("by=people")).Should().Be(1, "a person approved it");
        (await Total("by=autonomous")).Should().Be(0, "no rule replayed anything");
        (await Total("ending=watching")).Should().Be(1, "it was just sent, so it is still inside its watch window");
        (await Total("ending=unproven")).Should().Be(0, "the window has not closed yet");
        (await Total("ending=fixed")).Should().Be(0);
        (await Total("window=24h")).Should().Be(1);
        (await Total("q=no-such-thing")).Should().Be(0);
        (await host.Client.GetAsync("/api/v1/replays?provider=Aws&ending=bogus")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_promotion_names_the_failure_and_the_levels_it_moved_between()
    {
        var (host, _, _) = await Held(1);
        using var _h = host;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            var owner = (await db.Namespaces.FirstAsync()).OwnerId;
            (await scope.ServiceProvider.GetRequiredService<IRecoveryLedger>().RecordAutonomyGrantTransitionAsync(
                owner, "sig-timeout", RecoveryOperationKind.Replay, AutonomyLevel.Approve, AutonomyLevel.Standing, "test promotion", null)).IsSuccess.Should().BeTrue();
        }

        var activity = await Json(await host.Client.GetAsync("/api/v1/agents/autonomy-evaluation/activity"));
        var text = activity.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("text").GetString()).FirstOrDefault(t => t!.Contains("earned"));
        text.Should().Contain("Timeout on").And.Contain("L3 Approve → L4 Standing", "the line says which failure and which levels, not just 'a failure'");
    }

    [Fact]
    public async Task Approve_replays_as_the_person_through_the_gate_and_the_item_is_gone()
    {
        var (host, log, _) = await Held(1);
        using var _h = host;
        await RunAutoReplay(host);
        var entryId = (await Json(await host.Client.GetAsync("/api/v1/pending-work"))).GetProperty("items")[0].GetProperty("entryId").GetGuid();

        (await Post(host.Client, $"/api/v1/pending-work/{entryId}/approve", null)).StatusCode.Should().Be((HttpStatusCode)428);
        var approved = await Post(host.Client, $"/api/v1/pending-work/{entryId}/approve", "approve-escalation");

        approved.StatusCode.Should().Be(HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        log.Replays.Should().Be(1);
        (await Json(await host.Client.GetAsync("/api/v1/pending-work/count"))).GetProperty("total").GetInt32().Should().Be(0);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        var replayEntry = await db.RecoveryLedgerEntries.SingleAsync(e => e.State != RecoveryEntryState.Declined);
        var op = await db.RecoveryOperations.SingleAsync(o => o.Id == replayEntry.OperationId);
        op.ActorKind.Should().NotBe(RecoveryActorKind.Automation, "the approver — a person — replayed it, and the ledger says so");
        (await db.RecoveryEvents.CountAsync(e => e.EntryId == entryId && e.EventType == RecoveryEventType.OperatorNote)).Should().Be(1);

        (await Post(host.Client, $"/api/v1/pending-work/{entryId}/approve", "approve-escalation")).StatusCode.Should().Be(HttpStatusCode.NotFound, "answered once");
    }

    [Fact]
    public async Task Decline_needs_a_reason_records_it_deletes_nothing_and_is_not_asked_again()
    {
        var (host, log, _) = await Held(1);
        using var _h = host;
        await RunAutoReplay(host);
        var entryId = (await Json(await host.Client.GetAsync("/api/v1/pending-work"))).GetProperty("items")[0].GetProperty("entryId").GetGuid();

        (await Post(host.Client, $"/api/v1/pending-work/{entryId}/decline", "decline-escalation", new { reason = "" })).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Post(host.Client, $"/api/v1/pending-work/{entryId}/decline", "decline-escalation", new { reason = "Downstream still broken" }))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await Json(await host.Client.GetAsync("/api/v1/pending-work/count"))).GetProperty("total").GetInt32().Should().Be(0);
        await RunAutoReplay(host);
        (await Json(await host.Client.GetAsync("/api/v1/pending-work/count"))).GetProperty("total").GetInt32().Should().Be(0, "a no sticks until the failure changes");
        log.Replays.Should().Be(0);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        (await db.DlqMessages.SingleAsync()).Status.Should().Be(DlqMessageStatus.Active, "declining deletes nothing");
        (await db.RecoveryEvents.SingleAsync(e => e.EntryId == entryId && e.EventType == RecoveryEventType.OperatorNote)).DetailJson.Should().Contain("Downstream still broken");
    }
}
