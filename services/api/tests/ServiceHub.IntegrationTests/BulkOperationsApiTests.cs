using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Unit 3.2: bulk replay through the real host — no run without its stored preview; one gate check and one ledger entry per message.</summary>
public sealed class BulkOperationsApiTests : IDisposable
{
    public BulkOperationsApiTests() => Environment.SetEnvironmentVariable("BulkReplay__PerSecond", "50");

    public void Dispose() => Environment.SetEnvironmentVariable("BulkReplay__PerSecond", null);

    private static async Task<(DeadLettersApiTests.Handle Host, PeekLog Log, List<long> Ids)> Seeded(int count, Func<Result<bool>>? replay = null)
    {
        var log = new PeekLog { OnReplay = replay ?? (() => Result<bool>.Success(true)) };
        var host = DeadLettersApiTests.Host(log);
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, count);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        return (host, log, await db.DlqMessages.OrderBy(m => m.Id).Select(m => m.Id).ToListAsync());
    }

    private static async Task<JsonElement> Post(HttpClient client, string url, object body, string? intent = null, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (intent is not null) request.Headers.Add("X-ServiceHub-Intent", intent);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    private static async Task<JsonElement> WaitFor(HttpClient client, Guid id, params string[] statuses)
    {
        for (var i = 0; i < 100; i++)
        {
            var body = JsonDocument.Parse(await client.GetStringAsync($"/api/v1/bulk-operations/{id}")).RootElement;
            if (statuses.Contains(body.GetProperty("status").GetString())) return body;
            await Task.Delay(300);
        }

        throw new TimeoutException($"Bulk {id} never reached {string.Join("/", statuses)}.");
    }

    [Fact]
    public async Task A_preview_sends_nothing_and_says_what_would_happen()
    {
        var (host, log, ids) = await Seeded(3);
        using var _ = host;

        var preview = await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids });

        preview.GetProperty("selected").GetInt32().Should().Be(3);
        preview.GetProperty("willReplay").GetInt32().Should().Be(3);
        preview.GetProperty("heldBackCount").GetInt32().Should().Be(0);
        preview.GetProperty("groups")[0].GetProperty("reason").GetString().Should().Be("MaxDeliveryCountExceeded");
        log.Replays.Should().Be(0, "a preview must never touch the cloud");
    }

    [Fact]
    public async Task A_message_no_longer_in_the_queue_is_held_back_with_its_reason_and_a_remedy_never_a_bare_count()
    {
        var (host, _, ids) = await Seeded(2);
        using var _ = host;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            (await db.DlqMessages.FindAsync(ids[0]))!.Status = DlqMessageStatus.Resolved;
            await db.SaveChangesAsync();
        }

        var preview = await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids });

        preview.GetProperty("willReplay").GetInt32().Should().Be(1);
        var held = preview.GetProperty("heldBack").EnumerateArray().Single();
        held.GetProperty("reasonCode").GetString().Should().Be("NOT_ACTIVE");
        held.GetProperty("remedy").GetString().Should().Contain("nothing to replay");
        held.GetProperty("dlqMessageId").GetInt64().Should().Be(ids[0]);
    }

    [Fact]
    public async Task A_run_cannot_start_without_a_stored_preview()
    {
        var (host, log, ids) = await Seeded(1);
        using var _ = host;

        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = Guid.NewGuid() }, "bulk-replay", HttpStatusCode.NotFound);
        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = Guid.NewGuid() }, intent: null, HttpStatusCode.BadRequest);

        log.Replays.Should().Be(0);
        ids.Should().NotBeEmpty();
    }

    [Fact]
    public async Task A_started_preview_replays_every_message_with_its_own_ledger_entry_and_the_chain_verifies()
    {
        var (host, log, ids) = await Seeded(6);
        using var _ = host;
        var preview = await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids });
        var id = preview.GetProperty("previewId").GetGuid();

        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = id }, "bulk-replay", HttpStatusCode.Accepted);
        var done = await WaitFor(host.Client, id, "completed");

        done.GetProperty("sent").GetInt32().Should().Be(6);
        log.Replays.Should().Be(6);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        (await db.RecoveryLedgerEntries.CountAsync()).Should().Be(6, "one ledger entry per message");
        (await db.BulkOperationItems.CountAsync(i => i.RecoveryEntryId != null)).Should().Be(6);
    }

    [Fact]
    public async Task A_preview_can_be_started_only_once()
    {
        var (host, _, ids) = await Seeded(1);
        using var _ = host;
        var id = (await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids })).GetProperty("previewId").GetGuid();

        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = id }, "bulk-replay", HttpStatusCode.Accepted);
        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = id }, "bulk-replay", HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task It_stops_itself_after_consecutive_refusals_and_sends_nothing_further()
    {
        var (host, log, ids) = await Seeded(9, () => Result<bool>.Failure(Error.NotFound("Message.NotFound", "gone")));
        using var _ = host;
        var id = (await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids })).GetProperty("previewId").GetGuid();

        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = id }, "bulk-replay", HttpStatusCode.Accepted);
        var done = await WaitFor(host.Client, id, "stopped");

        done.GetProperty("failed").GetInt32().Should().Be(5);
        done.GetProperty("remaining").GetInt32().Should().Be(0, "unsent messages are skipped, not left half-queued");
        done.GetProperty("endedReason").GetString().Should().Contain("Stopped itself");
        log.Replays.Should().Be(5);
    }

    [Fact]
    public async Task A_sample_of_one_sends_exactly_one()
    {
        var (host, log, ids) = await Seeded(4);
        using var _ = host;
        var id = (await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids })).GetProperty("previewId").GetGuid();

        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = id, sampleOnly = true }, "bulk-replay", HttpStatusCode.Accepted);
        var done = await WaitFor(host.Client, id, "completed");

        done.GetProperty("sent").GetInt32().Should().Be(1);
        log.Replays.Should().Be(1);
    }

    [Fact]
    public async Task A_cancelled_preview_cannot_be_started()
    {
        var (host, _, ids) = await Seeded(1);
        using var _ = host;
        var id = (await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids })).GetProperty("previewId").GetGuid();

        await Post(host.Client, $"/api/v1/bulk-operations/{id}/cancel", new { });
        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = id }, "bulk-replay", HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Cancelling_mid_run_stops_before_the_next_message_and_leaves_the_ledger_consistent()
    {
        Environment.SetEnvironmentVariable("BulkReplay__PerSecond", "4");
        var (host, log, ids) = await Seeded(20);
        using var _ = host;
        var id = (await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids })).GetProperty("previewId").GetGuid();
        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = id }, "bulk-replay", HttpStatusCode.Accepted);

        for (var i = 0; i < 100 && log.Replays < 3; i++) await Task.Delay(100);
        await Post(host.Client, $"/api/v1/bulk-operations/{id}/cancel", new { });
        var done = await WaitFor(host.Client, id, "cancelled");

        var sent = done.GetProperty("sent").GetInt32();
        sent.Should().BeInRange(3, 19);
        done.GetProperty("remaining").GetInt32().Should().Be(0);
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        (await db.RecoveryLedgerEntries.CountAsync()).Should().Be(sent, "every message sent has an entry, and no unsent message has one");
        (await db.BulkOperationItems.CountAsync(i => i.State == BulkItemState.Skipped)).Should().Be(20 - sent);
    }

    [Fact]
    public async Task A_concurrent_scan_resolving_a_message_mid_replay_never_kills_the_run()
    {
        // The DLQ monitor marks a message resolved the moment it leaves the queue — which is the moment a replay of it lands.
        // The replay service carries on past that conflict; the run must too, and must never report the rest "interrupted".
        DeadLettersApiTests.Handle? host = null;
        var log = new PeekLog
        {
            OnReplay = () =>
            {
                using var scope = host!.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
                // Sequential and ascending, so the first still-active message is the one being replayed right now.
                var current = db.DlqMessages.Where(m => m.Status == DlqMessageStatus.Active).OrderBy(m => m.Id).First();
                current.Status = DlqMessageStatus.Resolved;
                current.ResolutionCause = DlqResolutionCause.VanishedExternally;
                db.SaveChanges();
                return Result<bool>.Success(true);
            },
        };
        host = DeadLettersApiTests.Host(log);
        using var _ = host;
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 6);
        List<long> ids;
        using (var scope = host.Services.CreateScope())
        {
            ids = await scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>().DlqMessages.Select(m => m.Id).ToListAsync();
        }

        var id = (await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids })).GetProperty("previewId").GetGuid();
        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = id }, "bulk-replay", HttpStatusCode.Accepted);

        var done = await WaitFor(host.Client, id, "completed", "stopped");

        done.GetProperty("status").GetString().Should().Be("completed");
        done.GetProperty("unknown").GetInt32().Should().Be(0, "nothing was interrupted");
    }

    [Theory]
    [InlineData("orders-topic/subscriptions/billing")] // Azure
    [InlineData("orders-topic/billing")]               // AWS and Google, as some adapters spell it
    public async Task A_message_under_a_topic_subscription_is_replayed_with_the_topic_and_the_bare_subscription_name(string storedName)
    {
        var log = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        using var host = DeadLettersApiTests.Host(log);
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        long id;
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            var owner = (await db.Namespaces.FindAsync(ns))!.OwnerId;
            var row = new DlqMessage
            {
                MessageId = "sub-1", SequenceNumber = 7, BodyHash = "h", NamespaceId = ns, CloudProvider = CloudProviderType.Azure, OwnerId = owner,
                EntityName = storedName, EntityType = ServiceBusEntityType.Subscription, TopicName = "orders-topic",
                EnqueuedTimeUtc = DateTimeOffset.UtcNow, DetectedAtUtc = DateTimeOffset.UtcNow, DeadLetterReason = "MaxDeliveryCountExceeded", DeliveryCount = 10,
            };
            db.DlqMessages.Add(row);
            await db.SaveChangesAsync();
            id = row.Id;
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/dead-letters/{id}/replay");
        request.Headers.Add("X-ServiceHub-Intent", "replay-message");
        (await host.Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.OK);

        // The cloud is asked for the TOPIC and the subscription's own name — never the stored path, which no cloud knows.
        log.LastReplayTarget.Should().Be(("orders-topic", "billing"));
    }
}
