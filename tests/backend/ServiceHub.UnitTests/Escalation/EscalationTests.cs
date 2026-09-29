using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Agents;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Recovery;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Escalation;

/// <summary>Units 5.2 and 5.6: exactly one event per escalation, none for a Deny, and a stalled agent raised once per episode.</summary>
public sealed class EscalationTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        await using var db = NewDb();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _connection.DisposeAsync();

    private ServiceHubDbContext NewDb() => new(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite(_connection).Options);

    private sealed class RecordingBus : IPlatformEventBus
    {
        public List<PlatformEvent> Published { get; } = [];
        public ValueTask PublishAsync(PlatformEvent platformEvent, CancellationToken cancellationToken = default) { Published.Add(platformEvent); return ValueTask.CompletedTask; }
        public void Subscribe(Func<PlatformEvent, CancellationToken, Task> handler) { }
    }

    private static readonly RecoveryActor RuleActor = new("System:AutoReplay:1", RecoveryActorKind.Automation);

    private async Task<(Namespace Ns, DlqMessage Message, AutoReplayRule Rule)> SeedAsync(ServiceHubDbContext db)
    {
        var ns = Namespace.Create("sqs.us-east-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:secretsecretsecretsecretsecretsecretsecr", provider: CloudProviderType.Aws, awsRegion: "us-east-1", ownerId: "owner1").Value;
        var message = new DlqMessage
        {
            MessageId = "m-1", SequenceNumber = 1, BodyHash = "h", NamespaceId = ns.Id, CloudProvider = CloudProviderType.Aws, OwnerId = "owner1",
            EntityName = "orders", EntityType = ServiceBusEntityType.Queue, EnqueuedTimeUtc = DateTimeOffset.UtcNow, DetectedAtUtc = DateTimeOffset.UtcNow,
        };
        var rule = new AutoReplayRule { OwnerId = "owner1", Name = "r", Provider = CloudProviderType.Aws, Reason = "Timeout", CreatedAt = DateTimeOffset.UtcNow };
        db.DlqMessages.Add(message);
        db.AutoReplayRules.Add(rule);
        await db.SaveChangesAsync();
        return (ns, message, rule);
    }

    [Fact]
    public async Task An_escalation_raises_exactly_one_event_carrying_the_gates_reason_code_and_a_retry_raises_none()
    {
        await using var db = NewDb();
        var (ns, message, rule) = await SeedAsync(db);
        var bus = new RecordingBus();
        var recorder = new EscalationRecorder(db, new RecoveryLedgerService(db), NullLogger<EscalationRecorder>.Instance, bus);
        var escalate = new EligibilityDecision(EligibilityVerdict.Escalate, "PROVIDER_CANNOT_VERIFY_ABSENCE");

        (await recorder.RecordHeldReplayAsync(message, ns, rule, RuleActor, escalate, default)).Should().BeTrue();
        (await recorder.RecordHeldReplayAsync(message, ns, rule, RuleActor, escalate, default)).Should().BeFalse("one per escalation, not one per retry");

        var evt = bus.Published.Should().ContainSingle().Subject;
        evt.EventType.Should().Be(EventTypes.EscalationRaised);
        var payload = (EscalationRaisedPayload)evt.Payload!;
        payload.ReasonCode.Should().Be("PROVIDER_CANNOT_VERIFY_ABSENCE", "the code is the valuable part — never flattened");
        payload.Reason.Should().Be(EscalationReasons.Describe("PROVIDER_CANNOT_VERIFY_ABSENCE"));
        payload.Entity.Should().Be("orders");
    }

    [Fact]
    public async Task A_deny_is_not_approvable_so_it_is_neither_recorded_nor_announced()
    {
        await using var db = NewDb();
        var (ns, message, rule) = await SeedAsync(db);
        var bus = new RecordingBus();
        var recorder = new EscalationRecorder(db, new RecoveryLedgerService(db), NullLogger<EscalationRecorder>.Instance, bus);

        (await recorder.RecordHeldReplayAsync(message, ns, rule, RuleActor, new EligibilityDecision(EligibilityVerdict.Deny, "PRODUCTION_ELEVATION_REQUIRED"), default)).Should().BeFalse();

        bus.Published.Should().BeEmpty();
        (await db.RecoveryLedgerEntries.CountAsync()).Should().Be(0);
    }

    private sealed class StuckAgent : IAgent
    {
        public AgentDescriptor Descriptor { get; } = new("stuck", "Stuck", "Never finishes.", AgentKind.Watch, AgentAuthority.Observes, TimeSpan.FromSeconds(10));
        public Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct) => Task.FromResult(AgentCycleResult.Idle());
    }

    [Fact]
    public async Task A_stalled_agent_becomes_pending_work_and_is_announced_once_per_episode()
    {
        var registry = new AgentRegistry([new StuckAgent()]);
        registry.RecordSuccess("stuck", AgentCycleResult.Idle(), DateTimeOffset.UtcNow.AddMinutes(-10)); // last cycle ten minutes ago

        var bus = new RecordingBus();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ServiceHubDbContext>(o => o.UseSqlite(_connection));
        services.AddSingleton<IPlatformEventBus>(bus);
        services.AddScoped<IRecoveryLedger>(sp => new RecoveryLedgerService(sp.GetRequiredService<ServiceHubDbContext>()));
        services.AddScoped<EscalationRecorder>();
        services.AddScoped<INamespaceRepository, NamespaceRepository>();
        await using var provider = services.BuildServiceProvider();
        await using (var db = NewDb())
        {
            await SeedAsync(db);
            db.Namespaces.Add(Namespace.Create("orders-dev", "Endpoint=sb://x.servicebus.windows.net/;SharedAccessKeyName=a;SharedAccessKey=b=", ownerId: "owner1").Value);
            await db.SaveChangesAsync();
        }

        var watchdog = new AgentWatchdog(registry, provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<AgentWatchdog>.Instance);
        await watchdog.CheckAsync(default);
        await watchdog.CheckAsync(default);

        var raised = bus.Published.Should().ContainSingle("one event per episode, not one per check").Subject;
        ((EscalationRaisedPayload)raised.Payload!).ReasonCode.Should().Be(EscalationReasons.AgentStale);

        var pending = await new PendingWorkService(NewDb(), registry).ListAsync(new PendingWorkScope("owner1", null), 10, default);
        pending.Items.Should().ContainSingle(i => i.Kind == "agent" && i.AgentId == "stuck");

        registry.RecordSuccess("stuck", AgentCycleResult.Idle(), DateTimeOffset.UtcNow); // it recovers
        (await new PendingWorkService(NewDb(), registry).ListAsync(new PendingWorkScope("owner1", null), 10, default)).Total.Should().Be(0);
    }

}
