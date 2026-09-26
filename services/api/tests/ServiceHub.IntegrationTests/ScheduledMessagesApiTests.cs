using System.Net;
using System.Text.Json;
using FluentAssertions;
using ServiceHub.Core.Entities;

namespace ServiceHub.IntegrationTests;

/// <summary>Scheduled messages (unit 6.17): soonest first, with a short preview; a cloud without them says so, never "none due".</summary>
public sealed class ScheduledMessagesApiTests
{
    [Fact]
    public async Task Scheduled_messages_are_listed_soonest_first_with_a_short_preview()
    {
        var azure = new PeekLog();
        var soon = DateTimeOffset.UtcNow.AddMinutes(5);
        azure.Scheduled.Add(new Message { MessageId = "later", SequenceNumber = 2, Body = new string('x', 500), ScheduledEnqueueTime = soon.AddHours(1), SizeInBytes = 500 });
        azure.Scheduled.Add(new Message { MessageId = "soon", SequenceNumber = 1, Body = "{}", ScheduledEnqueueTime = soon, SizeInBytes = 2 });
        using var host = DeadLettersApiTests.Host(azure);
        var ns = await DeadLettersApiTests.Connect(host.Client, "azure");

        var body = JsonDocument.Parse(await host.Client.GetStringAsync($"/api/v1/namespaces/{ns}/messages/scheduled?entity=orders")).RootElement;
        var messages = body.GetProperty("messages").EnumerateArray().ToList();
        messages.Select(m => m.GetProperty("messageId").GetString()).Should().Equal("soon", "later");
        messages[1].GetProperty("bodyPreview").GetString()!.Length.Should().Be(201, "200 characters and an ellipsis");
    }

    [Fact]
    public async Task A_cloud_without_scheduled_messages_says_so_instead_of_an_empty_list()
    {
        using var host = DeadLettersApiTests.Host(new PeekLog(), new PeekLog());
        var ns = await DeadLettersApiTests.Connect(host.Client, "aws");

        var response = await host.Client.GetAsync($"/api/v1/namespaces/{ns}/messages/scheduled?entity=orders");
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain("no scheduled messages");
    }
}
