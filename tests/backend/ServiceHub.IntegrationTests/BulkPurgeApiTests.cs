using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Bulk purge (unit 6.15): the bulk replay's preview-then-run, with a reason, its own intent and PURGE typed.</summary>
public sealed class BulkPurgeApiTests : IDisposable
{
    public BulkPurgeApiTests() => Environment.SetEnvironmentVariable("BulkReplay__PerSecond", "50");

    public void Dispose() => Environment.SetEnvironmentVariable("BulkReplay__PerSecond", null);

    private static async Task<JsonElement> Post(HttpClient client, string url, object body, string? intent, HttpStatusCode expected)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        if (intent is not null) request.Headers.Add("X-ServiceHub-Intent", intent);
        var response = await client.SendAsync(request);
        response.StatusCode.Should().Be(expected, await response.Content.ReadAsStringAsync());
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Many_are_purged_one_by_one_through_the_ledger_only_after_PURGE_is_typed_and_never_by_a_replay_intent()
    {
        var aws = new PeekLog { OnPurge = Result.Success, OnReplay = () => Result<bool>.Success(true) };
        using var host = DeadLettersApiTests.Host(new PeekLog(), aws);
        var ns = await DeadLettersApiTests.Connect(host.Client, "aws");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Aws, 3);
        List<long> ids;
        using (var scope = host.Services.CreateScope())
        {
            ids = await scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>().DlqMessages.Select(m => m.Id).ToListAsync();
        }

        await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids, kind = "purge" }, null, HttpStatusCode.BadRequest);
        var preview = await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids, kind = "purge", reason = "test data" }, null, HttpStatusCode.OK);
        preview.GetProperty("kind").GetString().Should().Be("purge");
        preview.GetProperty("willReplay").GetInt32().Should().Be(3);
        var id = preview.GetProperty("previewId").GetGuid();

        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = id }, "bulk-replay", HttpStatusCode.Conflict);
        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = id, confirm = "purge" }, "purge-message", HttpStatusCode.BadRequest);
        aws.Purges.Should().Be(0);
        aws.Replays.Should().Be(0, "a replay intent must never set off a purge, or a replay instead");

        await Post(host.Client, "/api/v1/bulk-operations", new { previewId = id, confirm = "PURGE" }, "purge-message", HttpStatusCode.Accepted);
        for (var i = 0; i < 100 && aws.Purges < 3; i++) await Task.Delay(200);
        aws.Purges.Should().Be(3);
        aws.Replays.Should().Be(0);

        var entries = JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/recovery/entries?window=all&state=Discarded")).RootElement;
        entries.GetProperty("total").GetInt32().Should().Be(3, "one ledger entry per message");
    }

    [Fact]
    public async Task Where_the_cloud_cannot_delete_one_message_every_one_is_held_back_with_the_reason()
    {
        using var host = DeadLettersApiTests.Host(new PeekLog { OnPurge = Result.Success });
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 2);
        List<long> ids;
        using (var scope = host.Services.CreateScope())
        {
            ids = await scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>().DlqMessages.Select(m => m.Id).ToListAsync();
        }

        var preview = await Post(host.Client, "/api/v1/bulk-operations/preview", new { dlqMessageIds = ids, kind = "purge", reason = "x" }, null, HttpStatusCode.OK);
        preview.GetProperty("willReplay").GetInt32().Should().Be(0);
        preview.GetProperty("heldBack").EnumerateArray().Should().OnlyContain(h => h.GetProperty("reasonCode").GetString() == "PURGE_UNSUPPORTED");
    }
}
