using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Endpoints the endpoint-coverage gate (<c>tests/scripts/check-endpoint-coverage.sh</c>) found no test calling: the product
/// catalogue, the grants list, an entity's settings, and a notification channel's switch and removal.
/// </summary>
public sealed class ReadAndLifecycleEndpointsApiTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private static HttpRequestMessage Req(HttpMethod m, string url, string? intent = null, object? body = null)
    {
        var r = new HttpRequestMessage(m, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (intent is not null) r.Headers.Add("X-ServiceHub-Intent", intent);
        return r;
    }

    [Fact]
    public async Task The_coverage_catalogue_describes_the_product_the_same_for_every_caller()
    {
        using var host = DeadLettersApiTests.Host();

        var response = await host.Client.GetAsync("/api/v1/fleet/coverage");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var rows = (await Json(response)).GetProperty("rows");
        rows.GetArrayLength().Should().BeGreaterThan(0);
        var clouds = rows.EnumerateArray().Select(r => r.GetProperty("cloudName").GetString()).Distinct().ToList();
        clouds.Should().HaveCountGreaterThanOrEqualTo(3, "Azure, AWS and Google Cloud are all in the catalogue the product publishes");
        rows.EnumerateArray().Should().OnlyContain(r => !string.IsNullOrWhiteSpace(r.GetProperty("statusWords").GetString()), "every row says in words how far it is supported");
    }

    [Fact]
    public async Task Grants_are_listed_once_governance_is_on_and_can_be_filtered_to_one_identity()
    {
        using var host = DeadLettersApiTests.Host();
        (await Json(await host.Client.GetAsync("/api/v1/governance/grants"))).GetArrayLength().Should().Be(0, "nothing is granted until someone grants");

        (await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/governance/grants", "grant-role", new { granteeIdentity = "reader", granteeKind = "ApiKey", role = "Viewer" })))
            .StatusCode.Should().Be(HttpStatusCode.Created);

        var all = await Json(await host.Client.GetAsync("/api/v1/governance/grants"));
        all.EnumerateArray().Select(g => g.GetProperty("granteeIdentity").GetString()).Should().Contain("ApiKey:reader", "an identity is stored with its kind");
        var one = await Json(await host.Client.GetAsync("/api/v1/governance/grants?granteeIdentity=ApiKey:reader"));
        one.EnumerateArray().Should().OnlyContain(g => g.GetProperty("granteeIdentity").GetString() == "ApiKey:reader");
        (await Json(await host.Client.GetAsync("/api/v1/governance/grants?granteeIdentity=nobody"))).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_channel_can_be_switched_off_and_on_and_removing_it_needs_the_declared_intent()
    {
        using var host = DeadLettersApiTests.Host();
        var added = await Json(await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/settings/channels", "add-channel",
            new { format = "slack", label = "#ops", url = "https://hooks.slack.com/services/T0/B0/secretpart" })));
        var id = added.GetProperty("id").GetGuid();

        var off = await Json(await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/settings/channels/{id}/enabled", null, new { enabled = false })));
        off.GetProperty("enabled").GetBoolean().Should().BeFalse();
        var on = await Json(await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/settings/channels/{id}/enabled", null, new { enabled = true })));
        on.GetProperty("enabled").GetBoolean().Should().BeTrue();
        (await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/settings/channels/{Guid.NewGuid()}/enabled", null, new { enabled = true })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await host.Client.SendAsync(Req(HttpMethod.Delete, $"/api/v1/settings/channels/{id}"))).StatusCode.Should().Be((HttpStatusCode)428);
        (await host.Client.SendAsync(Req(HttpMethod.Delete, $"/api/v1/settings/channels/{id}", "remove-channel"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.Client.SendAsync(Req(HttpMethod.Delete, $"/api/v1/settings/channels/{id}", "remove-channel"))).StatusCode.Should().Be(HttpStatusCode.NotFound, "it is already gone");
        (await host.Client.GetStringAsync("/api/v1/settings")).Should().NotContain("#ops");
    }

    [Fact]
    public async Task An_entitys_settings_are_validated_then_read_through_the_provider_and_are_null_when_the_cloud_does_not_read_them()
    {
        using var host = DeadLettersApiTests.Host();
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");

        (await host.Client.GetAsync($"/api/v1/namespaces/{ns}/entities/settings?name=orders&kind=bucket")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.GetAsync($"/api/v1/namespaces/{ns}/entities/settings?kind=queue")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a name is required");
        (await host.Client.GetAsync($"/api/v1/namespaces/{Guid.NewGuid()}/entities/settings?name=orders&kind=queue")).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var read = await host.Client.GetAsync($"/api/v1/namespaces/{ns}/entities/settings?name=orders&kind=queue");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await Json(read);
        body.GetProperty("name").GetString().Should().Be("orders");
        body.GetProperty("kind").GetString().Should().Be("queue");
        body.GetProperty("settings").ValueKind.Should().Be(JsonValueKind.Null, "'not read' is null, never an empty list");
    }
}
