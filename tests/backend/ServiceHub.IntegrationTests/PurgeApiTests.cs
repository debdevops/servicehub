using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Purge (unit 6.15): through the gate and the ledger, only where the cloud can delete one message, with a reason.</summary>
public sealed class PurgeApiTests
{
    private static HttpRequestMessage Purge(long id, string? reason, bool intent = true)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/dead-letters/{id}/purge") { Content = JsonContent.Create(new { reason }) };
        if (intent) r.Headers.Add("X-ServiceHub-Intent", "purge-message");
        return r;
    }

    private static async Task<long> OnlyId(DeadLettersApiTests.Handle host, Guid ns)
    {
        using var scope = host.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>().DlqMessages.Single(m => m.NamespaceId == ns).Id;
    }

    [Fact]
    public async Task A_purge_is_recorded_before_and_after_the_cloud_acts_and_the_row_leaves_the_list()
    {
        var aws = new PeekLog { OnPurge = Result.Success };
        using var host = DeadLettersApiTests.Host(new PeekLog(), aws);
        var ns = await DeadLettersApiTests.Connect(host.Client, "aws");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Aws, 1);
        var id = await OnlyId(host, ns);

        (await host.Client.SendAsync(Purge(id, "poison", intent: false))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.SendAsync(Purge(id, "  "))).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a purge needs a reason");
        aws.Purges.Should().Be(0);

        var response = await host.Client.SendAsync(Purge(id, "poison message, confirmed with the team"));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var outcome = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        outcome.GetProperty("result").GetString().Should().Be("accepted");
        outcome.GetProperty("state").GetString().Should().Be("Discarded");
        aws.Purges.Should().Be(1);

        var list = JsonDocument.Parse(await host.Client.GetStringAsync($"/api/v1/dead-letters?namespaceId={ns}&status=all")).RootElement;
        var row = list.GetProperty("items").EnumerateArray().Single();
        row.GetProperty("status").GetString().Should().Be("resolved");
        row.GetProperty("resolutionCause").GetString().Should().Be("purgedByServiceHub");

        var entry = JsonDocument.Parse(await host.Client.GetStringAsync($"/api/v1/recovery/entries/{outcome.GetProperty("entryId").GetString()}")).RootElement;
        entry.GetRawText().Should().Contain("poison message, confirmed with the team");
        JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/recovery/chain")).RootElement.GetProperty("isValid").GetBoolean().Should().BeTrue();

        (await host.Client.SendAsync(Purge(id, "again"))).StatusCode.Should().Be(HttpStatusCode.Conflict, "it is gone; there is nothing to purge");
        aws.Purges.Should().Be(1, "never retried, never twice");
    }

    [Fact]
    public async Task A_cloud_that_cannot_delete_one_message_is_never_asked_to()
    {
        var azure = new PeekLog { OnPurge = Result.Success };
        using var host = DeadLettersApiTests.Host(azure);
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 1);

        var response = await host.Client.SendAsync(Purge(await OnlyId(host, ns), "poison"));
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("cannot delete one message");
        azure.Purges.Should().Be(0);
    }

    [Fact]
    public async Task A_lost_answer_is_unknown_not_failed()
    {
        var aws = new PeekLog { OnPurge = () => Result.Failure(Error.Internal("fake.timeout", "timed out")) };
        using var host = DeadLettersApiTests.Host(new PeekLog(), aws);
        var ns = await DeadLettersApiTests.Connect(host.Client, "aws");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Aws, 1);

        var outcome = JsonDocument.Parse(await (await host.Client.SendAsync(Purge(await OnlyId(host, ns), "poison"))).Content.ReadAsStringAsync()).RootElement;
        outcome.GetProperty("result").GetString().Should().Be("unknown");
        outcome.GetProperty("message").GetString().Should().Contain("before it could tell whether");
    }
}
