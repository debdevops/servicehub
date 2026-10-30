using System.Net;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Security;
using ServiceHub.Infrastructure.Webhooks;

namespace ServiceHub.UnitTests.Webhooks;

/// <summary>Plan G7: a connection can name the one channel that is told about it, and a choice that cannot be followed is never silence.</summary>
public sealed class OwnerChannelRoutingTests : IDisposable
{
    private const string Owner = "owner-1";
    private readonly ServiceHubDbContext _db;
    private readonly List<string> _hosts = [];
    private readonly WebhookChannelSender _sender;

    public OwnerChannelRoutingTests()
    {
        _db = new ServiceHubDbContext(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite("DataSource=:memory:").Options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();

        var protector = new Mock<IConnectionStringProtector>();
        protector.Setup(p => p.Unprotect(It.IsAny<string>())).Returns<string>(s => Result<string>.Success(s));
        var http = new Mock<IHttpClientFactory>();
        http.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(new Recording(_hosts)));
        var dns = new Mock<IDnsResolver>();
        dns.Setup(d => d.ResolveHostAddressesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([IPAddress.Parse("93.184.216.34")]);

        _sender = new WebhookChannelSender(
            _db, protector.Object, http.Object,
            [new GenericWebhookFormatter(), new SlackWebhookFormatter(), new TeamsWebhookFormatter()],
            dns.Object, Options.Create(new WebhookOptions()), NullLoggerFactory.Instance);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private sealed class Recording(List<string> hosts) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            hosts.Add(request.RequestUri!.Host);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private NotificationChannel Channel(string host, bool enabled = true)
    {
        var channel = new NotificationChannel
        {
            OwnerId = Owner, Format = WebhookFormat.Generic, Label = host, UrlEncrypted = $"https://{host}/hook", Enabled = enabled,
            CreatedAt = DateTimeOffset.UtcNow, CreatedBy = "test",
        };
        _db.NotificationChannels.Add(channel);
        return channel;
    }

    private async Task<Guid> ConnectionToldAboutAsync(Guid? channelId)
    {
        var name = $"bus-{Guid.NewGuid():N}"[..12];
        var ns = Namespace.Create(name, $"Endpoint=sb://{name}.servicebus.windows.net/;SharedAccessKeyName=k;SharedAccessKey=abc=", ownerId: Owner).Value;
        ns.AssignOwnerChannel(channelId);
        _db.Namespaces.Add(ns);
        await _db.SaveChangesAsync();
        return ns.Id;
    }

    private static EscalationRaisedPayload Escalation(Guid? namespaceId) => new()
    {
        Kind = "approval", ReasonCode = "AUTONOMY_GRANT_INSUFFICIENT", Reason = "A person decides.", RaisedAtUtc = DateTimeOffset.UtcNow, NamespaceId = namespaceId,
    };

    [Fact]
    public async Task A_connection_with_a_chosen_channel_tells_only_that_one()
    {
        var ops = Channel("ops.example.com");
        Channel("billing.example.com");
        await _db.SaveChangesAsync();
        var id = await ConnectionToldAboutAsync(ops.Id);

        await _sender.DeliverAsync(Escalation(id), Owner, CancellationToken.None);

        _hosts.Should().Equal("ops.example.com");
    }

    [Fact]
    public async Task A_connection_with_no_choice_tells_every_enabled_channel_as_before()
    {
        Channel("ops.example.com");
        Channel("billing.example.com");
        Channel("off.example.com", enabled: false);
        await _db.SaveChangesAsync();
        var id = await ConnectionToldAboutAsync(null);

        await _sender.DeliverAsync(Escalation(id), Owner, CancellationToken.None);

        _hosts.Should().BeEquivalentTo(["ops.example.com", "billing.example.com"]);
    }

    [Fact]
    public async Task A_chosen_channel_that_is_switched_off_or_gone_is_never_silence()
    {
        Channel("ops.example.com");
        var off = Channel("off.example.com", enabled: false);
        await _db.SaveChangesAsync();
        var switchedOff = await ConnectionToldAboutAsync(off.Id);

        await _sender.DeliverAsync(Escalation(switchedOff), Owner, CancellationToken.None);
        _hosts.Should().Equal("ops.example.com");

        _hosts.Clear();
        var gone = await ConnectionToldAboutAsync(Guid.NewGuid());
        await _sender.DeliverAsync(Escalation(gone), Owner, CancellationToken.None);
        _hosts.Should().Equal("ops.example.com");
    }

    [Fact]
    public async Task An_escalation_that_belongs_to_no_connection_tells_every_enabled_channel()
    {
        Channel("ops.example.com");
        Channel("billing.example.com");
        await _db.SaveChangesAsync();

        await _sender.DeliverAsync(Escalation(null), Owner, CancellationToken.None);

        _hosts.Should().BeEquivalentTo(["ops.example.com", "billing.example.com"]);
    }
}
