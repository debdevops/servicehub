using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Events.Payloads;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Agents;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Recovery;

namespace ServiceHub.UnitTests.Agents;

/// <summary>The Health Keeper's rules, one at a time, and the agent that announces each finding once per episode.</summary>
public sealed class HealthAgentTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 31, 12, 0, 0, TimeSpan.Zero);
    private static readonly EmergencyStopState Off = new(false, null, null, null);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceHubDbContext _db;
    private readonly Mock<IPlatformEventBus> _bus = new();
    private readonly Mock<IRecoveryLedger> _ledger = new();
    private readonly Mock<IAgentRegistry> _registry = new();
    private readonly Mock<INamespaceRepository> _namespaces = new();
    private readonly List<PlatformEvent> _events = [];
    private readonly FakeTime _time = new(Now);
    private readonly Namespace _ns = Namespace.Create(
        "sqs.us-east-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
        provider: CloudProviderType.Aws, ownerId: "owner-1", awsRegion: "us-east-1").Value;
    private readonly HealthAgent _agent;
    private readonly ServiceProvider _services;

    public HealthAgentTests()
    {
        _connection.Open();
        _db = new ServiceHubDbContext(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _bus.Setup(b => b.PublishAsync(It.IsAny<PlatformEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PlatformEvent, CancellationToken>((e, _) => _events.Add(e)).Returns(ValueTask.CompletedTask);
        _ledger.Setup(l => l.GetEmergencyStopStateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Off);
        _ledger.Setup(l => l.GetAgeingAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync([]);
        _ledger.Setup(l => l.VerifyChainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChainVerificationResult { OwnerId = "owner-1", IsValid = true, EventsChecked = 1 });
        _registry.Setup(r => r.All()).Returns([]);
        _namespaces.Setup(n => n.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success([_ns]));

        var services = new ServiceCollection();
        services.AddSingleton(_registry.Object);
        services.AddSingleton(_ledger.Object);
        services.AddSingleton(_namespaces.Object);
        services.AddSingleton(_db);
        services.AddSingleton(_bus.Object);
        services.AddSingleton<EscalationRecorder>(sp => new EscalationRecorder(_db, _ledger.Object, NullLogger<EscalationRecorder>.Instance, _bus.Object, _time));
        _services = services.BuildServiceProvider();
        _agent = new HealthAgent(_services.GetRequiredService<IServiceScopeFactory>(), NullLogger<HealthAgent>.Instance, _time);
    }

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class FakeTime(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Current { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Current;
    }

    private static AgentRuntimeState AgentState(string id, bool paused = false) =>
        new(new AgentDescriptor(id, id.ToUpperInvariant(), "p", AgentKind.Watch, AgentAuthority.Observes, TimeSpan.FromMinutes(1)), AgentHealth.Healthy, Now, null, null, 0, paused);

    private static RecoveryLedgerEntry Entry(RecoveryEntryState state, TimeSpan begunAgo, TimeSpan? windowEndedAgo = null) =>
        new() { OperationId = Guid.NewGuid(), OwnerId = "owner-1", BodyHash = "h", TargetEntity = "q", BegunAt = Now - begunAgo, State = state, ObservationWindowEndsAt = windowEndedAgo is null ? null : Now - windowEndedAgo };

    private static HealthSnapshot Snap(EmergencyStopState? stop = null, IReadOnlyList<AgentRuntimeState>? agents = null, IReadOnlyList<RecoveryLedgerEntry>? open = null,
        bool chainChecked = true, bool chainValid = true, IReadOnlyList<Namespace>? connections = null) =>
        new(stop ?? Off, agents ?? [], open ?? [], chainChecked, chainValid, connections ?? []);

    private static IReadOnlyList<string> Codes(HealthSnapshot s) => [.. HealthChecks.Evaluate(s, Now).Select(f => f.Code)];

    [Fact]
    public void A_healthy_system_has_no_findings()
    {
        Codes(Snap(agents: [AgentState("dlq-monitor")], open: [Entry(RecoveryEntryState.Executing, TimeSpan.FromSeconds(30))])).Should().BeEmpty();
    }

    [Fact]
    public void Emergency_stop_is_a_reminder_only_after_an_hour()
    {
        Codes(Snap(stop: new EmergencyStopState(true, "maya", Now - TimeSpan.FromMinutes(59), "deploy"))).Should().BeEmpty("a stop someone just switched on is not news");
        var late = HealthChecks.Evaluate(Snap(stop: new EmergencyStopState(true, "maya", Now - TimeSpan.FromHours(3), "deploy")), Now).Single();

        late.Code.Should().Be(EscalationReasons.HealthEmergencyStopOn);
        late.Reason.Should().Contain("3 hours").And.Contain("deploy");
    }

    [Fact]
    public void A_paused_agent_is_a_finding_but_the_health_keeper_does_not_report_itself()
    {
        var findings = HealthChecks.Evaluate(Snap(agents: [AgentState("auto-replay", paused: true), AgentState("health", paused: true), AgentState("backup")]), Now);

        findings.Should().ContainSingle().Which.Key.Should().Be("HEALTH_AGENT_PAUSED:auto-replay");
    }

    [Fact]
    public void An_attempt_executing_for_over_fifteen_minutes_is_stuck_and_a_shorter_one_is_not()
    {
        Codes(Snap(open: [Entry(RecoveryEntryState.Executing, TimeSpan.FromMinutes(14))])).Should().BeEmpty();
        var f = HealthChecks.Evaluate(Snap(open: [Entry(RecoveryEntryState.Executing, TimeSpan.FromMinutes(40)), Entry(RecoveryEntryState.Executing, TimeSpan.FromHours(2))]), Now).Single();

        f.Code.Should().Be(EscalationReasons.HealthLedgerStuck);
        f.Severity.Should().Be(EventSeverity.Error);
        f.Reason.Should().StartWith("2 recovery attempts have been running");
    }

    [Fact]
    public void An_unknown_outcome_is_not_double_reported_here_because_it_already_waits_for_a_person()
    {
        Codes(Snap(open: [Entry(RecoveryEntryState.ExecutionUnknown, TimeSpan.FromDays(2))])).Should().BeEmpty();
    }

    [Fact]
    public void A_replay_still_watched_two_hours_after_its_window_ended_means_verification_is_not_closing_entries()
    {
        Codes(Snap(open: [Entry(RecoveryEntryState.Observing, TimeSpan.FromDays(1), windowEndedAgo: TimeSpan.FromMinutes(90))])).Should().BeEmpty();
        Codes(Snap(open: [Entry(RecoveryEntryState.Observing, TimeSpan.FromDays(1), windowEndedAgo: TimeSpan.FromHours(3))])).Should().Equal(EscalationReasons.HealthObservationOverdue);
    }

    [Fact]
    public void A_broken_chain_is_critical_and_an_unchecked_one_says_nothing()
    {
        HealthChecks.Evaluate(Snap(chainValid: false), Now).Single().Severity.Should().Be(EventSeverity.Critical);
        Codes(Snap(chainChecked: false, chainValid: false)).Should().BeEmpty("a ledger that could not be read is not proof that it is broken");
    }

    [Fact]
    public void A_failed_connection_test_is_about_that_connection_and_carries_it_for_routing()
    {
        _ns.RecordConnectionTest(false);

        var f = HealthChecks.Evaluate(Snap(connections: [_ns]), Now).Single();

        f.Code.Should().Be(EscalationReasons.HealthConnectionFailing);
        f.NamespaceId.Should().Be(_ns.Id);
    }

    [Fact]
    public void A_connection_that_passed_or_was_never_tested_is_fine()
    {
        Codes(Snap(connections: [_ns])).Should().BeEmpty();
        _ns.RecordConnectionTest(true);
        Codes(Snap(connections: [_ns])).Should().BeEmpty();
    }

    [Fact]
    public async Task A_finding_is_announced_once_through_the_escalation_path_and_not_again_while_it_lasts()
    {
        _ns.RecordConnectionTest(false);

        var first = await _agent.ExecuteCycleAsync(default);
        var second = await _agent.ExecuteCycleAsync(default);

        first.Changed.Should().Be(1);
        first.Summary.Should().Contain(EscalationReasons.HealthConnectionFailing);
        second.Changed.Should().Be(0, "one announcement per episode, not one per cycle");
        second.Summary.Should().Contain("1 problem", "it is still a problem, only not news");
        var e = _events.Should().ContainSingle().Subject;
        e.EventType.Should().Be(EventTypes.EscalationRaised);
        e.Severity.Should().Be(EventSeverity.Error);
        e.NamespaceId.Should().Be(_ns.Id);
        var payload = e.Payload.Should().BeOfType<EscalationRaisedPayload>().Subject;
        payload.Kind.Should().Be("agent", "so every channel and the Open Agents link work unchanged");
        payload.AgentId.Should().Be("health");
    }

    [Fact]
    public async Task A_problem_that_clears_and_returns_is_announced_again()
    {
        _ns.RecordConnectionTest(false);
        await _agent.ExecuteCycleAsync(default);
        _ns.RecordConnectionTest(true);
        (await _agent.ExecuteCycleAsync(default)).Summary.Should().Be("all clear");

        _ns.RecordConnectionTest(false);
        await _agent.ExecuteCycleAsync(default);

        _events.Should().HaveCount(2);
    }

    [Fact]
    public async Task All_clear_announces_nothing()
    {
        var result = await _agent.ExecuteCycleAsync(default);

        result.Summary.Should().Be("all clear");
        _events.Should().BeEmpty();
    }

    [Fact]
    public async Task The_chain_is_verified_at_most_once_an_hour_and_a_broken_one_stays_a_finding_in_between()
    {
        _ledger.Setup(l => l.VerifyChainAsync("owner-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChainVerificationResult { OwnerId = "owner-1", IsValid = false, EventsChecked = 5 });

        await _agent.ExecuteCycleAsync(default);
        _time.Current = Now.AddMinutes(30);
        var again = await _agent.ExecuteCycleAsync(default);
        _time.Current = Now.AddMinutes(61);
        await _agent.ExecuteCycleAsync(default);

        _ledger.Verify(l => l.VerifyChainAsync("owner-1", It.IsAny<CancellationToken>()), Times.Exactly(2));
        again.Summary.Should().Contain(EscalationReasons.HealthLedgerChainBroken);
        _events.Should().ContainSingle("announced once while it lasts");
    }

    [Fact]
    public async Task An_unreadable_ledger_is_not_reported_as_broken()
    {
        _ledger.Setup(l => l.VerifyChainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new InvalidOperationException("db busy"));

        (await _agent.ExecuteCycleAsync(default)).Summary.Should().Be("all clear");
    }

    [Fact]
    public async Task With_no_connections_there_is_nothing_to_watch()
    {
        _namespaces.Setup(n => n.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Result<IReadOnlyList<Namespace>>.Success([]));

        (await _agent.ExecuteCycleAsync(default)).Summary.Should().Be("no connections to watch");
    }

    [Fact]
    public void It_declares_that_it_only_observes_and_may_not_act()
    {
        _agent.Descriptor.Authority.Should().Be(AgentAuthority.Observes);
        _agent.Descriptor.MayNot.Should().Contain(m => m.Contains("Replay, purge or redrive", StringComparison.Ordinal));
    }
}
