using System.Net;
using System.Text.Json;
using FluentAssertions;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// ServiceHub has no login, so a web page that rebinds its own DNS name to 127.0.0.1 could otherwise drive the whole API as the Administrator
/// from the visitor's browser. The defence is refusing any <c>Host</c> that is not ours. Proven live on 2026-10-03: before this, a request
/// with <c>Host: evil.example.com</c> got the backup list with a 200.
/// </summary>
public sealed class HostAllowListApiTests
{
    private static async Task<HttpResponseMessage> Get(HttpClient client, string path, string host)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Host = host;
        return await client.SendAsync(request);
    }

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("evil.example.com:5153")]
    [InlineData("localhost.evil.example.com")]
    [InlineData("127.0.0.1.nip.io")]
    public async Task A_request_for_a_host_that_is_not_ours_is_refused_before_anything_runs(string host)
    {
        using var root = new ServiceHubApiFactory();
        var client = root.CreateClient();

        var response = await Get(client, "/api/v1/admin/backup", host);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString().Should().Be("host_not_allowed");
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("localhost:5153")]
    [InlineData("LOCALHOST:3000")]
    [InlineData("127.0.0.1:8080")]
    [InlineData("[::1]:5153")]
    public async Task The_loopback_names_a_person_actually_uses_still_work(string host)
    {
        using var root = new ServiceHubApiFactory();
        var client = root.CreateClient();

        (await Get(client, "/api/v1/admin/backup", host)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task The_health_endpoints_answer_on_any_host_because_a_platform_probe_often_calls_them_by_an_internal_address()
    {
        using var root = new ServiceHubApiFactory();
        var client = root.CreateClient();

        (await Get(client, "/health", "10.0.0.7:8080")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Get(client, "/health/ready", "evil.example.com")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AllowedHosts_names_the_hosts_a_deployment_is_reached_by_and_everything_else_is_still_refused()
    {
        using var root = new ServiceHubApiFactory();
        using var factory = root.WithWebHostBuilder(b => b.UseSetting("AllowedHosts", "servicehub.contoso.com;*.internal.contoso.com"));
        var client = factory.CreateClient();

        (await Get(client, "/api/v1/admin/backup", "servicehub.contoso.com")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Get(client, "/api/v1/admin/backup", "app.internal.contoso.com:8443")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Get(client, "/api/v1/admin/backup", "evil.example.com")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await Get(client, "/api/v1/admin/backup", "internal.contoso.com")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "a wildcard covers subdomains, not the bare name");
        (await Get(client, "/api/v1/admin/backup", "localhost")).StatusCode.Should().Be(HttpStatusCode.BadRequest, "setting AllowedHosts replaces the loopback default");
    }

    [Fact]
    public async Task AllowedHosts_star_is_the_explicit_way_to_turn_the_check_off()
    {
        using var root = new ServiceHubApiFactory();
        using var factory = root.WithWebHostBuilder(b => b.UseSetting("AllowedHosts", "*"));

        (await Get(factory.CreateClient(), "/api/v1/admin/backup", "anything.example.com")).StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
