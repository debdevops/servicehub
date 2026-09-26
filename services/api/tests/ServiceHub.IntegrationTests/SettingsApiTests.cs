using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Units 6.3 and 6.10 through the real host: channels keep their secret, the SSRF guard covers every channel, and emergency stop is deliberate and real.</summary>
public sealed class SettingsApiTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private static HttpRequestMessage Req(HttpMethod m, string url, string? intent = null, object? body = null)
    {
        var r = new HttpRequestMessage(m, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (intent is not null) r.Headers.Add("X-ServiceHub-Intent", intent);
        return r;
    }

    [Fact]
    public async Task A_channel_url_is_a_secret_stored_encrypted_and_never_returned()
    {
        using var host = DeadLettersApiTests.Host();
        (await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/settings/channels", null, new { format = "slack", label = "#ops", url = "https://hooks.slack.com/services/T0/B0/secretpart" })))
            .StatusCode.Should().Be((HttpStatusCode)428);
        var added = await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/settings/channels", "add-channel", new { format = "slack", label = "#ops", url = "https://hooks.slack.com/services/T0/B0/secretpart" }));
        added.StatusCode.Should().Be(HttpStatusCode.Created);
        (await added.Content.ReadAsStringAsync()).Should().NotContain("secretpart");

        var settings = await host.Client.GetStringAsync("/api/v1/settings");
        settings.Should().NotContain("secretpart").And.Contain("#ops");
        JsonDocument.Parse(settings).RootElement.GetProperty("notifications").GetProperty("bellAlwaysOn").GetBoolean().Should().BeTrue();

        using var scope = host.Services.CreateScope();
        (await scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>().NotificationChannels.SingleAsync()).UrlEncrypted.Should().StartWith("ENC[");

        (await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/settings/channels", "add-channel", new { format = "generic", url = "http://example.com/x" })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "only https:// addresses are taken");
    }

    [Fact]
    public async Task A_test_to_a_private_address_is_refused_by_the_same_ssrf_guard_every_channel_goes_through()
    {
        using var host = DeadLettersApiTests.Host();
        var added = await Json(await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/settings/channels", "add-channel", new { format = "generic", label = "inside", url = "https://127.0.0.1/hook" })));

        var result = await Json(await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/settings/channels/{added.GetProperty("id").GetGuid()}/test")));

        result.GetProperty("delivered").GetBoolean().Should().BeFalse("a loopback address must never be reached from a webhook");
        result.GetProperty("error").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Emergency_stop_needs_a_reason_and_the_typed_word_then_stops_automatic_replays_until_switched_off()
    {
        var aws = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        using var host = DeadLettersApiTests.Host(aws);
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");

        (await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/settings/emergency-stop", "emergency-stop", new { active = true, reason = "incident" })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest, "switching it on must be deliberate: STOP typed");
        var on = await Json(await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/settings/emergency-stop", "emergency-stop", new { active = true, reason = "incident 42", confirm = "STOP" })));
        on.GetProperty("active").GetBoolean().Should().BeTrue();
        on.GetProperty("reason").GetString().Should().Be("incident 42");

        // The gate itself now refuses an automatic actor.
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 1);
        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            var nsEntity = await db.Namespaces.FindAsync(ns);
            var id = (await db.DlqMessages.FirstAsync()).Id;
            var decision = await scope.ServiceProvider.GetRequiredService<IDlqReplayService>().CheckEligibilityAsync(
                id, nsEntity!, new RecoveryActor("System:AutoReplay:1", RecoveryActorKind.Automation), RecoveryOperationKind.Replay, default);
            decision!.ReasonCode.Should().Be("EMERGENCY_STOP_ACTIVE");
        }

        (await Json(await host.Client.GetAsync("/api/v1/settings/emergency-stop"))).GetProperty("active").GetBoolean().Should().BeTrue();
        (await Json(await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/settings/emergency-stop", "emergency-stop", new { active = false }))))
            .GetProperty("active").GetBoolean().Should().BeFalse();
    }
}
