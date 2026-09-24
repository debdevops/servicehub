using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Unit 1.7 through the real host: who is acting, told honestly, and the durable history that
/// records it. With nothing configured — a valid deployment — every answer is "a browser session".
/// </summary>
public sealed class IdentityAndAuditApiTests
{
    private sealed class Host(ServiceHubApiFactory root, System.IDisposable factory, HttpClient client) : IDisposable
    {
        public HttpClient Client { get; } = client;

        public void Dispose()
        {
            Client.Dispose();
            factory.Dispose();
            root.Dispose();
        }
    }

    private static Host Start(params (string Key, string Value)[] settings)
    {
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(builder =>
        {
            foreach (var (key, value) in settings)
            {
                builder.UseSetting(key, value);
            }

            builder.ConfigureServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider)).ToList())
                {
                    services.Remove(d);
                }

                services.AddScoped<ICloudMessagingProvider>(_ => new FakeProvider(CloudProviderType.Azure, ProviderCapabilities.Azure, failProbes: false));
            });
        });
        return new Host(root, factory, factory.CreateClient());
    }

    private static async Task<JsonElement> Json(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    private static HttpRequestMessage Connect(string name, params (string Header, string Value)[] headers)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/namespaces")
        {
            Content = JsonContent.Create(new
            {
                name,
                connectionString = $"Endpoint=sb://{name}.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=v==",
                authType = "connectionString",
                provider = "azure",
            }),
        };
        request.Headers.Add("X-ServiceHub-Intent", "create-namespace");
        foreach (var (header, value) in headers)
        {
            request.Headers.Add(header, value);
        }

        return request;
    }

    [Fact]
    public async Task With_nothing_configured_me_answers_a_browser_session_and_does_not_fail()
    {
        using var host = Start();

        var response = await host.Client.GetAsync("/api/v1/me");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var me = await Json(response);
        me.GetProperty("authMethod").GetString().Should().Be("session");
        me.GetProperty("actor").GetProperty("isSession").GetBoolean().Should().BeTrue();
        me.GetProperty("actor").GetProperty("label").GetString().Should().Be("from this browser session");
        me.GetProperty("effectiveRole").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_browser_session_id_is_carried_but_a_malformed_one_is_dropped()
    {
        using var host = Start();

        var good = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        good.Headers.Add("X-ServiceHub-Session", "3f2b9c1e-7a64-4d0a");
        var bad = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        bad.Headers.Add("X-ServiceHub-Session", "<script>x</script>");

        (await Json(await host.Client.SendAsync(good))).GetProperty("actor").GetProperty("identity").GetString()
            .Should().Be("session:3f2b9c1e-7a64-4d0a");
        (await Json(await host.Client.SendAsync(bad))).GetProperty("actor").GetProperty("identity").GetString()
            .Should().Be("session");
    }

    [Fact]
    public async Task A_configured_api_key_is_named_and_a_wrong_one_is_a_401_with_a_code()
    {
        using var host = Start(("Security:Authentication:ApiKeys:0:Key", "real-key-value"), ("Security:Authentication:ApiKeys:0:Description", "ops-bot"));

        var named = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        named.Headers.Add("X-API-KEY", "real-key-value");
        var me = await Json(await host.Client.SendAsync(named));
        me.GetProperty("authMethod").GetString().Should().Be("ApiKey");
        me.GetProperty("actor").GetProperty("identity").GetString().Should().Be("ApiKey:ops-bot");
        me.GetProperty("actor").GetProperty("kind").GetString().Should().Be("apiKey");

        var wrong = new HttpRequestMessage(HttpMethod.Get, "/api/v1/me");
        wrong.Headers.Add("X-API-KEY", "guess");
        var refused = await host.Client.SendAsync(wrong);
        refused.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await Json(refused)).GetProperty("code").GetString().Should().Be("invalid_api_key");

        // No key at all is still a browser session: attribution, not a gate.
        (await host.Client.GetAsync("/api/v1/me")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Connecting_and_removing_a_namespace_leave_named_rows_that_outlive_it()
    {
        using var host = Start(("Security:Authentication:ApiKeys:0:Key", "real-key-value"), ("Security:Authentication:ApiKeys:0:Description", "ops-bot"));

        var created = await host.Client.SendAsync(Connect("acme-bus", ("X-API-KEY", "real-key-value")));
        var id = (await Json(created)).GetProperty("id").GetGuid();

        var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/v1/namespaces/{id}");
        delete.Headers.Add("X-ServiceHub-Intent", "delete-namespace");
        delete.Headers.Add("X-API-KEY", "real-key-value");
        (await host.Client.SendAsync(delete)).StatusCode.Should().Be(HttpStatusCode.NoContent);

        var audit = await Json(await host.Client.GetAsync("/api/v1/audit"));
        audit.GetProperty("total").GetInt32().Should().Be(2);

        var items = audit.GetProperty("items").EnumerateArray().ToList();
        items.Select(i => i.GetProperty("action").GetString()).Should().Equal("Namespace.Remove", "Namespace.Connect");
        items.Should().OnlyContain(i => i.GetProperty("actor").GetProperty("identity").GetString() == "ApiKey:ops-bot");
        items.Should().OnlyContain(i => i.GetProperty("namespaceName").GetString() == "acme-bus");
        items.Should().OnlyContain(i => i.GetProperty("outcome").GetString() == "Success");
        items.Should().OnlyContain(i => i.GetProperty("cloudProvider").GetString() == "azure");
    }

    [Fact]
    public async Task With_no_identity_the_trail_says_from_this_browser_session_and_invents_no_name()
    {
        using var host = Start();
        await host.Client.SendAsync(Connect("acme-bus"));

        var entry = (await Json(await host.Client.GetAsync("/api/v1/audit"))).GetProperty("items")[0];

        entry.GetProperty("actor").GetProperty("label").GetString().Should().Be("from this browser session");
        entry.GetProperty("actor").GetProperty("isSession").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task The_trail_pages_newest_first_and_filters_by_namespace()
    {
        using var host = Start();
        var first = (await Json(await host.Client.SendAsync(Connect("first-bus")))).GetProperty("id").GetGuid();
        await host.Client.SendAsync(Connect("second-bus"));

        var page1 = await Json(await host.Client.GetAsync("/api/v1/audit?pageSize=1"));
        page1.GetProperty("items").GetArrayLength().Should().Be(1);
        page1.GetProperty("total").GetInt32().Should().Be(2);
        page1.GetProperty("items")[0].GetProperty("namespaceName").GetString().Should().Be("second-bus");

        var filtered = await Json(await host.Client.GetAsync($"/api/v1/audit?namespaceId={first}"));
        filtered.GetProperty("total").GetInt32().Should().Be(1);
        filtered.GetProperty("items")[0].GetProperty("namespaceName").GetString().Should().Be("first-bus");
    }

    [Fact]
    public async Task A_refused_request_writes_no_audit_row()
    {
        using var host = Start();
        var request = Connect("acme-bus");
        request.Headers.Remove("X-ServiceHub-Intent");

        (await host.Client.SendAsync(request)).StatusCode.Should().Be(HttpStatusCode.PreconditionRequired);

        (await Json(await host.Client.GetAsync("/api/v1/audit"))).GetProperty("total").GetInt32().Should().Be(0);
    }
}
