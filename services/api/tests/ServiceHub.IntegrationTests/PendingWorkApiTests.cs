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
