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
    public async Task Signatures_are_counted_over_one_namespace_or_one_environment_when_asked()
    {
        using var host = DeadLettersApiTests.Host();
        var azure = await DeadLettersApiTests.Connect(host.Client, "azure");
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");
        await DeadLettersApiTests.Seed(host, azure, CloudProviderType.Azure, 5, reason: "Timeout");
        await DeadLettersApiTests.Seed(host, aws, CloudProviderType.Aws, 3, reason: "Validation", prefix: "v");
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            typeof(Namespace).GetProperty(nameof(Namespace.Environment))!.SetValue(await db.Namespaces.FindAsync(aws), EnvironmentType.Prod);
            await db.SaveChangesAsync();
        }

        await Sign(host);

        (await Get(host.Client, "/api/v1/signatures")).GetProperty("total").GetInt32().Should().Be(2);
        var prod = await Get(host.Client, "/api/v1/signatures?environment=Prod");
        prod.GetProperty("total").GetInt32().Should().Be(1);
        prod.GetProperty("items")[0].GetProperty("reason").GetString().Should().Be("Validation");
        (await Get(host.Client, "/api/v1/signatures?environment=Uat")).GetProperty("total").GetInt32().Should().Be(0);

        var one = await Get(host.Client, $"/api/v1/signatures?namespaceId={azure}");
        one.GetProperty("total").GetInt32().Should().Be(1);
        one.GetProperty("items")[0].GetProperty("messages").GetInt32().Should().Be(5);

        // Each signature says where it was seen: which namespace, in which environment, and how many of its messages are there.
        var where = (await Get(host.Client, "/api/v1/signatures?provider=aws")).GetProperty("items")[0].GetProperty("namespaces").EnumerateArray().ToList();
        where.Should().ContainSingle();
        where[0].GetProperty("id").GetGuid().Should().Be(aws);
        where[0].GetProperty("environment").GetString().Should().BeOneOf("prod", "Prod");
        where[0].GetProperty("messages").GetInt32().Should().Be(3);
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

    [Fact]
    public async Task What_a_failure_has_earned_says_how_far_it_is_from_replaying_on_its_own_and_why()
    {
        using var host = DeadLettersApiTests.Host();
        var azure = await DeadLettersApiTests.Connect(host.Client, "azure");
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");
        await DeadLettersApiTests.Seed(host, azure, CloudProviderType.Azure, 2, reason: "Timeout");
        await DeadLettersApiTests.Seed(host, aws, CloudProviderType.Aws, 2, reason: "Timeout", prefix: "a");
        await Sign(host);

        var azureHash = (await Get(host.Client, "/api/v1/signatures?provider=azure")).GetProperty("items")[0].GetProperty("signatureHash").GetString();
        var awsHash = (await Get(host.Client, "/api/v1/signatures?provider=aws")).GetProperty("items")[0].GetProperty("signatureHash").GetString();

        var onAzure = await Get(host.Client, $"/api/v1/signatures/{azureHash}/trust?provider=azure");
        onAzure.GetProperty("level").GetString().Should().Be("approve", "everything starts with a person approving each replay");
        onAzure.GetProperty("sampleSize").GetInt32().Should().Be(0);
        onAzure.TryGetProperty("verifiedSuccessRate", out var rate).Should().BeTrue();
        rate.ValueKind.Should().Be(JsonValueKind.Null, "no outcomes is not a rate of zero");
        onAzure.GetProperty("nextLevel").GetString().Should().Be("standing");
        onAzure.GetProperty("moreVerifiedNeeded").GetInt32().Should().Be(10);
        onAzure.GetProperty("cloudCanConfirm").GetBoolean().Should().BeTrue();

        var onAws = await Get(host.Client, $"/api/v1/signatures/{awsHash}/trust?provider=aws");
        onAws.GetProperty("cloudCanConfirm").GetBoolean().Should().BeFalse();
        onAws.GetProperty("nextLevel").ValueKind.Should().Be(JsonValueKind.Null, "a cloud that cannot confirm an outcome never earns automatic replay");

        await Get(host.Client, $"/api/v1/signatures/nope/trust?provider=azure", HttpStatusCode.NotFound);
        await Get(host.Client, $"/api/v1/signatures/{azureHash}/trust", HttpStatusCode.BadRequest);
    }
}
