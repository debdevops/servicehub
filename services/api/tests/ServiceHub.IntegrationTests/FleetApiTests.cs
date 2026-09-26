using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Unit 3.5: the fleet view, through the real host — each cloud on its own numbers, capability computed, silence not health.</summary>
public sealed class FleetApiTests
{
    private static async Task<JsonElement> Overview(HttpClient client, string query = "", HttpStatusCode expected = HttpStatusCode.OK)
    {
        var response = await client.GetAsync("/api/v1/fleet/overview" + query);
        response.StatusCode.Should().Be(expected);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task Two_clouds_show_two_computed_capability_states_and_no_blended_totals()
    {
        using var host = DeadLettersApiTests.Host();
        var azure = await DeadLettersApiTests.Connect(host.Client, "azure");
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");
        await DeadLettersApiTests.Seed(host, azure, CloudProviderType.Azure, 4, reason: "Timeout");
        await DeadLettersApiTests.Seed(host, azure, CloudProviderType.Azure, 2, reason: "Validation", prefix: "v");
        await DeadLettersApiTests.Seed(host, aws, CloudProviderType.Aws, 9, reason: "Timeout", prefix: "w");

        var fleet = await Overview(host.Client);
        var clouds = fleet.GetProperty("clouds").EnumerateArray().ToDictionary(c => c.GetProperty("provider").GetString()!);

        clouds["azure"].GetProperty("capability").GetString().Should().Be("canConfirm");
        clouds["aws"].GetProperty("capability").GetString().Should().Be("observerRequired");
        clouds["azure"].GetProperty("active").GetInt32().Should().Be(6);
        // AWS is not watched: what ServiceHub happens to have recorded is not its dead-letter count.
        clouds["aws"].GetProperty("watched").GetBoolean().Should().BeFalse();
        clouds["aws"].GetProperty("active").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Top_failures_are_one_row_per_cloud_and_reason_never_a_sum()
    {
        using var host = DeadLettersApiTests.Host();
        var azure = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, azure, CloudProviderType.Azure, 4, reason: "Timeout");
        await DeadLettersApiTests.Seed(host, azure, CloudProviderType.Azure, 2, reason: "Validation", prefix: "v");

        var top = (await Overview(host.Client)).GetProperty("topFailures").EnumerateArray().ToList();

        top.Select(t => (t.GetProperty("provider").GetString(), t.GetProperty("reason").GetString(), t.GetProperty("count").GetInt32()))
            .Should().Equal(("azure", "Timeout", 4), ("azure", "Validation", 2));
    }

    [Fact]
    public async Task Top_failures_say_which_environment_they_are_in_so_they_can_be_grouped_like_everything_else()
    {
        using var host = DeadLettersApiTests.Host();
        var azure = await DeadLettersApiTests.Connect(host.Client, "azure");
        var aws = await DeadLettersApiTests.Connect(host.Client, "aws");
        await DeadLettersApiTests.Seed(host, azure, CloudProviderType.Azure, 4, reason: "Timeout");
        await DeadLettersApiTests.Seed(host, aws, CloudProviderType.Aws, 3, reason: "Timeout");
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            typeof(Namespace).GetProperty(nameof(Namespace.Environment))!.SetValue(await db.Namespaces.FindAsync(aws), EnvironmentType.Prod);
            await db.SaveChangesAsync();
        }

        var top = (await Overview(host.Client)).GetProperty("topFailures").EnumerateArray().ToList();

        top.Select(t => (t.GetProperty("provider").GetString(), t.GetProperty("environment").GetString()?.ToLowerInvariant(), t.GetProperty("count").GetInt32()))
            .Should().Equal(("azure", "dev", 4), ("aws", "prod", 3));
    }

    [Fact]
    public async Task New_and_resolved_count_only_inside_the_window()
    {
        using var host = DeadLettersApiTests.Host();
        var azure = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, azure, CloudProviderType.Azure, 3, prefix: "new");
        await DeadLettersApiTests.Seed(host, azure, CloudProviderType.Azure, 5, age: TimeSpan.FromDays(3), prefix: "old");

        (await Overview(host.Client, "?window=24h")).GetProperty("clouds")[0].GetProperty("newInWindow").GetInt32().Should().Be(3);
        (await Overview(host.Client, "?window=7d")).GetProperty("clouds")[0].GetProperty("newInWindow").GetInt32().Should().Be(8);
    }

    [Fact]
    public async Task A_namespace_ServiceHub_cannot_watch_is_cannot_tell_never_healthy()
    {
        using var host = DeadLettersApiTests.Host();
        await DeadLettersApiTests.Connect(host.Client, "aws");

        var ns = (await Overview(host.Client)).GetProperty("namespaces")[0];

        ns.GetProperty("health").GetString().Should().Be("cannotTell");
    }

    [Fact]
    public async Task A_bad_window_is_a_sentence_not_a_guess()
    {
        using var host = DeadLettersApiTests.Host();
        await Overview(host.Client, "?window=fortnight", HttpStatusCode.BadRequest);
    }
}
