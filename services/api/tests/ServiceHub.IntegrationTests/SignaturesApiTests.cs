using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Signatures;

namespace ServiceHub.IntegrationTests;

/// <summary>Unit 3.8: two real failure modes are two signatures, with correct counts and outcomes.</summary>
public sealed class SignaturesApiTests
{
    private static async Task<JsonElement> Get(HttpClient client, string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.GetAsync(url);
        response.StatusCode.Should().Be(expected);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    /// <summary>The scanner signs what it records; the seed helper does not, so sign them the same way here.</summary>
    private static async Task Sign(DeadLettersApiTests.Handle host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        foreach (var m in db.DlqMessages.Where(m => m.SignatureHash == null).ToList())
        {
            m.SignatureHash = await SignatureRecorder.AssignAsync(db, m, default);
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Two_failure_modes_are_two_signatures_with_the_right_counts()
    {
        using var host = DeadLettersApiTests.Host();
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 5, reason: "Timeout");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 2, reason: "Validation", prefix: "v");
        await Sign(host);

        var page = await Get(host.Client, "/api/v1/signatures?provider=azure");

        page.GetProperty("total").GetInt32().Should().Be(2);
        var items = page.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => (i.GetProperty("reason").GetString(), i.GetProperty("messages").GetInt32())).Should().Equal(("Timeout", 5), ("Validation", 2));
        items[0].GetProperty("replayVerdict").GetString().Should().Be("unknown", "nothing was replayed, so it is not claimed to help or not");
        items[0].GetProperty("daily").GetArrayLength().Should().Be(7);
    }

    [Fact]
    public async Task Replay_outcomes_come_from_the_ledger_and_unverified_is_not_counted_as_either()
    {
        using var host = DeadLettersApiTests.Host();
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 4, reason: "Timeout");
        await Sign(host);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            var owner = db.Namespaces.Single(n => n.Id == ns).OwnerId;
            var ids = db.DlqMessages.OrderBy(m => m.Id).Select(m => m.Id).ToList();
            var states = new[] { RecoveryEntryState.Recovered, RecoveryEntryState.Recovered, RecoveryEntryState.Returned, RecoveryEntryState.Unverified };
            for (var i = 0; i < 4; i++)
            {
                var entry = new RecoveryLedgerEntry { OperationId = Guid.NewGuid(), OwnerId = owner, BodyHash = "h", TargetEntity = "orders", BegunAt = DateTimeOffset.UtcNow, State = states[i] };
                db.RecoveryLedgerEntries.Add(entry);
                db.ReplayHistories.Add(new ReplayHistory { DlqMessageId = ids[i], RecoveryEntryId = entry.Id, OwnerId = owner, NamespaceId = ns, MessageId = $"m{i}", SourceEntity = "orders", ReplayedAt = DateTimeOffset.UtcNow, ReplayedBy = "u", ReplayStrategy = "x", ReplayedToEntity = "orders", OutcomeStatus = "accepted" });
            }

            await db.SaveChangesAsync();
        }

        var s = (await Get(host.Client, "/api/v1/signatures?provider=azure")).GetProperty("items")[0];

        var r = s.GetProperty("replays");
        (r.GetProperty("replayed").GetInt32(), r.GetProperty("stayedFixed").GetInt32(), r.GetProperty("returned").GetInt32(), r.GetProperty("unverified").GetInt32())
            .Should().Be((4, 2, 1, 1));
        s.GetProperty("replayVerdict").GetString().Should().Be("helps");
        (await Get(host.Client, "/api/v1/signatures?provider=azure&tab=helps")).GetProperty("total").GetInt32().Should().Be(1);
        (await Get(host.Client, "/api/v1/signatures?provider=azure&tab=doesnt")).GetProperty("total").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task A_signature_is_found_by_its_hash_and_a_bad_tab_or_hash_is_refused()
    {
        using var host = DeadLettersApiTests.Host();
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 3, reason: "Timeout");
        await Sign(host);
        var hash = (await Get(host.Client, "/api/v1/signatures?provider=azure")).GetProperty("items")[0].GetProperty("signatureHash").GetString();

        (await Get(host.Client, $"/api/v1/signatures/{hash}?provider=azure")).GetProperty("messages").GetInt32().Should().Be(3);
        await Get(host.Client, "/api/v1/signatures/nope?provider=azure", HttpStatusCode.NotFound);
        await Get(host.Client, "/api/v1/signatures?tab=bogus", HttpStatusCode.BadRequest);
    }
}
