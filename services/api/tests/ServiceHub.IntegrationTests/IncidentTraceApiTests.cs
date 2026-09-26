using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.IntegrationTests;

/// <summary>Incident and Trace (unit 6.19): from recorded tables only, never a live look into a cloud.</summary>
public sealed class IncidentTraceApiTests
{
    private static async Task Add(DeadLettersApiTests.Handle host, Guid ns, CloudProviderType cloud, string id, string? correlation, string? signature, DateTimeOffset at)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        var owner = (await db.Namespaces.FindAsync(ns))!.OwnerId;
        db.DlqMessages.Add(new DlqMessage
        {
            MessageId = id, SequenceNumber = Random.Shared.NextInt64(1, long.MaxValue), BodyHash = "b", NamespaceId = ns, CloudProvider = cloud, OwnerId = owner,
            EntityName = "orders", EntityType = ServiceBusEntityType.Queue, EnqueuedTimeUtc = at, DetectedAtUtc = at, DeliveryCount = 3, MessageSize = 10,
            CorrelationId = correlation, SignatureHash = signature, DeadLetterReason = "Timeout", Status = DlqMessageStatus.Active,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task A_correlation_id_is_traced_across_clouds_in_time_order_with_what_was_done()
    {
        var aws = new PeekLog { OnPurge = Result.Success };
        using var host = DeadLettersApiTests.Host(new PeekLog(), aws);
        var azure = await DeadLettersApiTests.Connect(host.Client, "azure");
        var awsNs = await DeadLettersApiTests.Connect(host.Client, "aws");
        var t = DateTimeOffset.UtcNow.AddHours(-3);
        await Add(host, azure, CloudProviderType.Azure, "az-1", "order-42", null, t);
        await Add(host, awsNs, CloudProviderType.Aws, "aw-1", "order-42", null, t.AddHours(1));
        await Add(host, awsNs, CloudProviderType.Aws, "other", "order-43", null, t);

        long awsId;
        using (var scope = host.Services.CreateScope())
        {
            awsId = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>().DlqMessages.Single(m => m.MessageId == "aw-1").Id;
        }

        var purge = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/dead-letters/{awsId}/purge") { Content = JsonContent.Create(new { reason = "trace test" }) };
        purge.Headers.Add("X-ServiceHub-Intent", "purge-message");
        (await host.Client.SendAsync(purge)).EnsureSuccessStatusCode();
        var looksBefore = aws.Calls;

        var trace = JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/trace?correlationId=order-42")).RootElement;
        trace.GetProperty("clouds").EnumerateArray().Select(c => c.GetString()).Should().BeEquivalentTo(["azure", "aws"]);
        var hops = trace.GetProperty("hops").EnumerateArray().ToList();
        hops.Select(h => h.GetProperty("kind").GetString()).Should().Equal("dead_lettered", "dead_lettered", "purged");
        hops.Select(h => h.GetProperty("place").GetProperty("provider").GetString()).Should().Equal("azure", "aws", "aws");
        aws.Calls.Should().Be(looksBefore, "a trace reads what was recorded; it never looks into a cloud");

        (await host.Client.GetAsync("/api/v1/trace")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/trace?correlationId=nobody")).RootElement.GetProperty("hops").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_signature_incident_tells_its_story_newest_first()
    {
        using var host = DeadLettersApiTests.Host();
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");
        var t = DateTimeOffset.UtcNow.AddDays(-2);
        await Add(host, ns, CloudProviderType.Azure, "s1", null, "sig-1", t);
        await Add(host, ns, CloudProviderType.Azure, "s2", null, "sig-1", t.AddDays(1));
        await Add(host, ns, CloudProviderType.Azure, "s3", null, "sig-1", t.AddDays(1).AddMinutes(5));

        var body = JsonDocument.Parse(await host.Client.GetStringAsync("/api/v1/signatures/sig-1/incident?provider=Azure")).RootElement;
        body.GetProperty("messages").GetInt32().Should().Be(3);
        var timeline = body.GetProperty("timeline").EnumerateArray().ToList();
        timeline.Select(i => i.GetProperty("kind").GetString()).Should().Equal("came_back", "first_seen");
        timeline[0].GetProperty("text").GetString().Should().StartWith("2 more");

        (await host.Client.GetAsync("/api/v1/signatures/sig-1/incident")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.GetAsync("/api/v1/signatures/sig-1/incident?provider=Aws")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
