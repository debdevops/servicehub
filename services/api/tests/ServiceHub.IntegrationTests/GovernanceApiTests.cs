using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Unit 5.7 through the real host: governance changes nothing until a grant exists; a Viewer is refused an Operator action with
/// the reason and the grantor named; the people not individually restricted keep their access; and revoking a restricted
/// identity's grant never hands it the owner's Admin (the privilege escalation found live on 2026-09-19).
/// </summary>
public sealed class GovernanceApiTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private static HttpRequestMessage Req(HttpMethod method, string url, string? key = null, string? intent = null, object? body = null)
    {
        var r = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (key is not null) r.Headers.Add("X-API-KEY", key);
        if (intent is not null) r.Headers.Add("X-ServiceHub-Intent", intent);
        return r;
    }

    private static async Task<(DeadLettersApiTests.Handle Host, PeekLog Log, long Id)> Seeded()
    {
        var log = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(b =>
        {
            // Per host, never process-wide: other test classes run in parallel.
            b.UseSetting("Security:Authentication:ApiKeys:0:Key", "reader-key-value");
            b.UseSetting("Security:Authentication:ApiKeys:0:Description", "reader");
            b.UseSetting("Security:Authentication:ApiKeys:1:Key", "lead-key-value");
            b.UseSetting("Security:Authentication:ApiKeys:1:Description", "lead");
            b.ConfigureServices(services =>
            {
                foreach (var d in services.Where(d => d.ServiceType == typeof(ICloudMessagingProvider)).ToList()) services.Remove(d);
                services.AddSingleton<ICloudMessagingProvider>(new PeekableProvider(CloudProviderType.Azure, ProviderCapabilities.Azure, log));
            });
        });
        var host = new DeadLettersApiTests.Handle(root, factory);
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 2);
        var id = (await Json(await host.Client.GetAsync($"/api/v1/dead-letters?namespaceId={ns}"))).GetProperty("items")[0].GetProperty("id").GetInt64();
        return (host, log, id);
    }

    [Fact]
    public async Task Until_a_grant_exists_everyone_is_admin_and_nothing_is_refused()
    {
        var (host, log, id) = await Seeded();
        using var _ = host;
        var me = await Json(await host.Client.SendAsync(Req(HttpMethod.Get, "/api/v1/me", "reader-key-value")));
        me.GetProperty("effectiveRole").GetString().Should().Be("Admin");
        me.GetProperty("governanceActive").GetBoolean().Should().BeFalse();
        (await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/dead-letters/{id}/replay", "reader-key-value", "replay-message"))).StatusCode.Should().Be(HttpStatusCode.OK);
        log.Replays.Should().Be(1);
    }

    [Fact]
    public async Task A_viewer_is_refused_with_the_reason_and_the_grantor_named_and_a_revoke_never_restores_admin()
    {
        var (host, log, id) = await Seeded();
        using var _ = host;
        // Turn governance on: the lead is Admin, the reader a Viewer. The browser session is not individually restricted.
        (await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/governance/grants", null, "grant-role", new { granteeIdentity = "lead", granteeKind = "ApiKey", role = "Admin" })))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        var viewerGrant = await Json(await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/governance/grants", "lead-key-value", "grant-role", new { granteeIdentity = "reader", granteeKind = "ApiKey", role = "Viewer" })));
        var viewerGrantId = viewerGrant.GetProperty("id").GetGuid();

        var refused = await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/dead-letters/{id}/replay", "reader-key-value", "replay-message"));
        refused.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await Json(refused);
        problem.GetProperty("code").GetString().Should().Be("permission_denied", "never a bare 403");
        problem.GetProperty("requiredRole").GetString().Should().Be("Operator");
        problem.GetProperty("yourRole").GetString().Should().Be("Viewer");
        problem.GetProperty("grantors").EnumerateArray().Select(g => g.GetString()).Should().Contain("ApiKey:lead");
        problem.GetProperty("detail").GetString().Should().Contain("Operator").And.Contain("ApiKey:lead");
        log.Replays.Should().Be(0);

        var me = await Json(await host.Client.SendAsync(Req(HttpMethod.Get, "/api/v1/me", "reader-key-value")));
        me.GetProperty("effectiveRole").GetString().Should().Be("Viewer");
        me.GetProperty("governanceActive").GetBoolean().Should().BeTrue();

        // Everyone not individually restricted keeps the owner's Admin (4.0.0's seeder rule).
        (await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/dead-letters/{id}/replay", null, "replay-message"))).StatusCode.Should().Be(HttpStatusCode.OK);

        // Revoking the Viewer's only grant must NOT fall back to the owner-level Admin grant.
        (await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/governance/grants/{viewerGrantId}/revoke", "lead-key-value", "revoke-role"))).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/governance/grants", "reader-key-value", "grant-role", new { granteeIdentity = "reader", granteeKind = "ApiKey", role = "Admin" })))
            .StatusCode.Should().Be(HttpStatusCode.Forbidden, "a revoked identity stays differentiated: revoke means no access, never full access");
    }
}
