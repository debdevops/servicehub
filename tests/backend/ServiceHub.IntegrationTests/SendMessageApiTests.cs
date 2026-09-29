using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Send a message (unit 6.14): one message, intent header, audited, refused in Production.</summary>
public sealed class SendMessageApiTests
{
    private static HttpRequestMessage Send(Guid ns, object body, bool intent = true)
    {
        var r = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/namespaces/{ns}/messages") { Content = JsonContent.Create(body) };
        if (intent) r.Headers.Add("X-ServiceHub-Intent", "send-message");
        return r;
    }

    [Fact]
    public async Task One_message_reaches_the_cloud_with_its_properties_and_is_audited()
    {
        var azure = new PeekLog();
        using var host = DeadLettersApiTests.Host(azure);
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        var body = new { entity = "orders", isTopic = false, body = "{\"order\":1}", contentType = "application/json", properties = new Dictionary<string, string> { ["source"] = "servicehub" } };

        (await host.Client.SendAsync(Send(ns, body, intent: false))).StatusCode.Should().Be((HttpStatusCode)428);
        azure.Sent.Should().BeEmpty();

        var response = await host.Client.SendAsync(Send(ns, body));
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("accepted one message onto orders");
        var sent = azure.Sent.Should().ContainSingle().Subject;
        sent.EntityName.Should().Be("orders");
        sent.ContentType.Should().Be("application/json");
        sent.ApplicationProperties!["source"].Should().Be("servicehub");

        var audit = await host.Client.GetStringAsync("/api/v1/audit?action=Message.Send");
        audit.Should().Contain("Message.Send").And.Contain("Success");
    }

    [Fact]
    public async Task Production_empty_bodies_and_a_cloud_that_refuses_are_all_said_plainly()
    {
        var azure = new PeekLog();
        using var host = DeadLettersApiTests.Host(azure);
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");

        (await host.Client.SendAsync(Send(ns, new { entity = "orders", body = "" }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.SendAsync(Send(ns, new { entity = "orders", body = new string('x', 300 * 1024) }))).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.SendAsync(Send(ns, new { entity = "broken", body = "x" }))).IsSuccessStatusCode.Should().BeFalse();

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            typeof(Namespace).GetProperty(nameof(Namespace.Environment))!.SetValue(await db.Namespaces.FindAsync(ns), EnvironmentType.Prod);
            await db.SaveChangesAsync();
        }

        var prod = await host.Client.SendAsync(Send(ns, new { entity = "orders", body = "x" }));
        prod.StatusCode.Should().Be(HttpStatusCode.Conflict);
        JsonDocument.Parse(await prod.Content.ReadAsStringAsync()).RootElement.GetProperty("detail").GetString().Should().Contain("Production");
        azure.Sent.Should().BeEmpty();
    }
}
