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

/// <summary>
/// Resolving an attempt whose answer was lost releases the block that stops a message being sent twice, so it is a decision with
/// a name on it: the server (not the screen) requires the Approver role, the person's words are written to the evidence ledger
/// under their identity, and nothing in the record says the message was recovered.
/// </summary>
public sealed class ResolveUnknownApiTests
{
    private static async Task<JsonElement> Json(HttpResponseMessage r) => JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

    private static HttpRequestMessage Req(HttpMethod method, string url, string? key = null, string? intent = null, object? body = null)
    {
        var r = new HttpRequestMessage(method, url) { Content = body is null ? null : JsonContent.Create(body) };
        if (key is not null) r.Headers.Add("X-API-KEY", key);
        if (intent is not null) r.Headers.Add("X-ServiceHub-Intent", intent);
        return r;
    }

    /// <summary>A host with a reader and a lead, one dead letter, and one attempt on it whose answer was never recorded.</summary>
    private static async Task<(DeadLettersApiTests.Handle Host, PeekLog Log, long Id, Guid Entry)> Unresolved()
    {
        var log = new PeekLog { OnReplay = () => Result<bool>.Success(true) };
        var root = new ServiceHubApiFactory();
        var factory = root.WithWebHostBuilder(b =>
        {
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
        await DeadLettersApiTests.Seed(host, ns, CloudProviderType.Azure, 1);

        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        var message = await db.DlqMessages.AsNoTracking().FirstAsync();
        var space = await db.Namespaces.AsNoTracking().FirstAsync();
        var ledger = scope.ServiceProvider.GetRequiredService<IRecoveryLedger>();
        var actor = new RecoveryActor("session", RecoveryActorKind.User);
        var op = (await ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = space.OwnerId, Kind = RecoveryOperationKind.Replay, Trigger = RecoveryTrigger.Manual, Actor = actor,
            NamespaceId = space.Id, NamespaceNameSnapshot = space.Name, ProviderSnapshot = space.Provider, EnvironmentSnapshot = space.Environment,
            ScopeDescription = "entity=orders", TargetCount = 1,
        })).Value;
        var entry = (await ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = op.Id, OwnerId = space.OwnerId, Actor = actor, DlqMessageId = message.Id, NamespaceId = space.Id,
            NamespaceNameSnapshot = space.Name, ProviderSnapshot = space.Provider, EnvironmentSnapshot = space.Environment,
            EntityNameSnapshot = message.EntityName, SourceMessageIdSnapshot = message.MessageId, BodyHash = message.BodyHash, TargetEntity = message.EntityName,
        })).Value;
        (await ledger.RecordExecutionAsync(new RecordExecutionRequest { EntryId = entry.Id, OwnerId = space.OwnerId, Actor = actor, Outcome = RecoveryExecutionOutcome.Unknown })).IsSuccess.Should().BeTrue();
        return (host, log, message.Id, entry.Id);
    }

    private static async Task TurnGovernanceOn(DeadLettersApiTests.Handle host)
    {
        (await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/governance/grants", null, "grant-role", new { granteeIdentity = "lead", granteeKind = "ApiKey", role = "Admin" })))
            .StatusCode.Should().Be(HttpStatusCode.Created);
        (await host.Client.SendAsync(Req(HttpMethod.Post, "/api/v1/governance/grants", "lead-key-value", "grant-role", new { granteeIdentity = "reader", granteeKind = "ApiKey", role = "Viewer" })))
            .StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task A_viewer_is_refused_by_the_server_and_nothing_changes_while_an_admin_can_resolve_it()
    {
        var (host, log, id, entry) = await Unresolved();
        using var _ = host;
        await TurnGovernanceOn(host);

        // The reader holds only Viewer. The check is the server's, whatever the screen shows.
        var denied = await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/pending-work/{entry}/resolve", "reader-key-value", "resolve-unknown-outcome", new { reason = "I looked" }));
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await Json(denied);
        problem.GetProperty("code").GetString().Should().Be("permission_denied");
        problem.GetProperty("requiredRole").GetString().Should().Be("Approver");
        problem.GetProperty("yourRole").GetString().Should().Be("Viewer");

        using (var scope = host.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
            (await db.RecoveryLedgerEntries.AsNoTracking().SingleAsync()).State.Should().Be(RecoveryEntryState.ExecutionUnknown, "a refused person changed nothing");
        }

        // Having a high role does not skip the block: a replay is still refused until the attempt is resolved.
        (await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/dead-letters/{id}/replay", "lead-key-value", "replay-message"))).StatusCode.Should().Be(HttpStatusCode.Conflict);
        log.Replays.Should().Be(0);

        var resolved = await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/pending-work/{entry}/resolve", "lead-key-value", "resolve-unknown-outcome", new { reason = "main queue holds one copy; nothing more to send" }));
        resolved.StatusCode.Should().Be(HttpStatusCode.NoContent);

        using var after = host.Services.CreateScope();
        var context = after.ServiceProvider.GetRequiredService<ServiceHubDbContext>();
        var closed = await context.RecoveryLedgerEntries.AsNoTracking().SingleAsync();
        closed.State.Should().Be(RecoveryEntryState.WrittenOff);
        closed.Disposition.Should().Be(RecoveryDisposition.WrittenOff, "not Recovered, not Failed: nobody proved either");

        // The words and the name are in the evidence, and the chain still verifies.
        var note = await context.RecoveryEvents.AsNoTracking().Where(e => e.EntryId == entry && e.EventType == RecoveryEventType.DispositionSet).SingleAsync();
        note.DetailJson.Should().Contain("a person checked").And.Contain("main queue holds one copy");
        note.ActorIdentity.Should().Contain("lead", "the person who closed it is named, not 'the system'");
        note.ActorKind.Should().NotBe(RecoveryActorKind.Automation);
        (await after.ServiceProvider.GetRequiredService<IRecoveryLedger>().VerifyChainAsync(closed.OwnerId)).IsValid.Should().BeTrue();
        (await context.RecoveryEvents.AsNoTracking().Where(e => e.EntryId == entry).Select(e => e.EventType).ToListAsync())
            .Should().NotContain(RecoveryEventType.NoRecurrenceObserved, "resolving is not a verdict that the message stayed fixed");
    }

    [Fact]
    public async Task It_can_only_be_resolved_once_and_only_with_a_reason()
    {
        var (host, _, _, entry) = await Unresolved();
        using var _ = host;

        (await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/pending-work/{entry}/resolve", null, "resolve-unknown-outcome", new { reason = "   " })))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/pending-work/{entry}/resolve", null, "resolve-unknown-outcome", new { reason = "nothing was sent" })))
            .StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/pending-work/{entry}/resolve", null, "resolve-unknown-outcome", new { reason = "again" })))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task An_approval_cannot_be_used_to_resolve_it_and_a_resolve_cannot_replay()
    {
        var (host, log, _, entry) = await Unresolved();
        using var _ = host;

        // The unresolved item is not an approval: approving or declining it by id finds nothing, and nothing is sent.
        (await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/pending-work/{entry}/approve", null, "approve-escalation"))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await host.Client.SendAsync(Req(HttpMethod.Post, $"/api/v1/pending-work/{entry}/decline", null, "decline-escalation", new { reason = "no" }))).StatusCode.Should().Be(HttpStatusCode.NotFound);
        log.Replays.Should().Be(0);
    }
}
