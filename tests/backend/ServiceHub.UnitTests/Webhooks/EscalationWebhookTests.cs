using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Webhooks;

namespace ServiceHub.UnitTests.Webhooks;

/// <summary>Unit 5.5: every format says what stopped, where, why and what to do — never "an escalation occurred".</summary>
public sealed class EscalationWebhookTests
{
    private static readonly EscalationNotification Approval = new(
        "approval", "PROVIDER_CANNOT_VERIFY_ABSENCE", "This cloud can't prove a replayed message stayed fixed. A person decides.",
        "orders-dev", "aws", "orders-sqs", new DateTimeOffset(2026, 9, 26, 14, 22, 0, TimeSpan.Zero), "https://servicehub.example/?modal=approve&entry=1");

    private static string Json(object payload) =>
        JsonSerializer.Serialize(payload, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    [Fact]
    public void Slack_says_what_where_why_and_links_to_review()
    {
        var json = Json(new SlackWebhookFormatter().BuildEscalationPayload(Approval));
        json.Should().Contain("The Agent stopped and asked you").And.Contain("AWS · orders-dev · orders-sqs")
            .And.Contain("can't prove a replayed message stayed fixed").And.Contain("PROVIDER_CANNOT_VERIFY_ABSENCE")
            .And.Contain("Review in ServiceHub").And.Contain("modal=approve");
    }

    [Fact]
    public void Teams_carries_the_same_facts_as_a_message_card()
    {
        var json = Json(new TeamsWebhookFormatter().BuildEscalationPayload(Approval));
        json.Should().Contain("MessageCard").And.Contain("AWS · orders-dev · orders-sqs").And.Contain("What to do").And.Contain("Review in ServiceHub");
    }

    [Fact]
    public void Generic_json_has_named_fields_including_the_reason_code()
    {
        using var doc = JsonDocument.Parse(Json(new GenericWebhookFormatter().BuildEscalationPayload(Approval)));
        var root = doc.RootElement;
        root.GetProperty("event").GetString().Should().Be("escalation.raised");
        root.GetProperty("reasonCode").GetString().Should().Be("PROVIDER_CANNOT_VERIFY_ABSENCE");
        root.GetProperty("where").GetString().Should().Be("AWS · orders-dev · orders-sqs");
        root.GetProperty("whatToDo").GetString().Should().Contain("approve");
    }

    [Fact]
    public void An_agent_escalation_points_at_the_agents_page_not_at_an_approval()
    {
        var agent = new EscalationNotification("agent", "AGENT_STALE", "Dead-letter Monitor: stopped reporting.", null, null, null, DateTimeOffset.UtcNow, null);
        agent.Headline.Should().Be("An agent stopped working");
        agent.Where.Should().BeEmpty();
        Json(new SlackWebhookFormatter().BuildEscalationPayload(agent)).Should().Contain("An agent stopped working").And.Contain("Agents");
    }

    private sealed class RecordingDelivery : IEscalationDelivery
    {
        public int Escalations { get; private set; }
        public string? LastCode { get; private set; }
        public string? LastOwner { get; private set; }
        public bool Throw { get; init; }
        public Task DeliverAsync(EscalationRaisedPayload escalation, string ownerId, CancellationToken cancellationToken)
        {
            if (Throw) throw new HttpRequestException("slack is down");
            Escalations++; LastCode = escalation.ReasonCode; LastOwner = ownerId; return Task.CompletedTask;
        }
        public Task<(bool Delivered, string? Error)> SendTestAsync(Guid channelId, string ownerId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private static (WebhookEscalationHandler Handler, RecordingDelivery Notifier) Handler(bool throws = false)
    {
        var notifier = new RecordingDelivery { Throw = throws };
        var services = new ServiceCollection().AddScoped<IEscalationDelivery>(_ => notifier).BuildServiceProvider();
        return (new WebhookEscalationHandler(services.GetRequiredService<IServiceScopeFactory>(), NullLogger<WebhookEscalationHandler>.Instance), notifier);
    }

    private static PlatformEvent Escalation() => new()
    {
        Source = "test", Category = EventCategories.Escalation, EventType = EventTypes.EscalationRaised, Actor = "owner1",
        Payload = new EscalationRaisedPayload { Kind = "approval", ReasonCode = "AUTONOMY_GRANT_INSUFFICIENT", Reason = "x", RaisedAtUtc = DateTimeOffset.UtcNow },
    };

    [Fact]
    public async Task The_handler_delivers_each_escalation_and_ignores_everything_else()
    {
        var (handler, notifier) = Handler();
        await handler.HandleAsync(Escalation(), default);
        await handler.HandleAsync(new PlatformEvent { Source = "t", Category = "dlq", EventType = EventTypes.DlqMessageDetected }, default);
        notifier.Escalations.Should().Be(1);
        notifier.LastCode.Should().Be("AUTONOMY_GRANT_INSUFFICIENT");
        notifier.LastOwner.Should().Be("owner1", "each owner's own channels, never another's");
    }

    [Fact]
    public async Task A_delivery_failure_never_fails_the_escalation()
    {
        var (handler, _) = Handler(throws: true);
        var act = () => handler.HandleAsync(Escalation(), default);
        await act.Should().NotThrowAsync("notification is best-effort; the pending item is the truth");
    }
}
