using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Recovery;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.RecoveryLedger;

/// <summary>
/// The restart-recovery safety fix: an attempt whose answer was never recorded must not be repeated.
/// A process that stops after the cloud accepted a send but before the ledger heard of it leaves an <c>Executing</c> entry; these
/// tests prove what ServiceHub then does, using fresh service instances over the same database — the only honest model of a
/// restart, because nothing the first instance held in memory survives it.
/// </summary>
public sealed class RestartRecoverySafetyTests : IDisposable
{
    private const string Owner = "owner-1";
    private static readonly Guid NamespaceId = Guid.NewGuid();
    private static readonly RecoveryActor Person = new("session", RecoveryActorKind.User);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceHubDbContext _db;

    public RestartRecoverySafetyTests()
    {
        _connection.Open();
        _db = NewContext();
        _db.Database.Migrate();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private ServiceHubDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite(_connection).Options);

    /// <summary>A brand-new ledger over a brand-new context on the same database: what a restarted process has.</summary>
    private RecoveryLedgerService Restarted() => new(NewContext());

    private static async Task<RecoveryLedgerEntry> Begin(
        RecoveryLedgerService ledger, long? dlqId = 7, string messageId = "msg-1", string owner = Owner, string entity = "orders")
    {
        var op = (await ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = owner, Kind = RecoveryOperationKind.Replay, Trigger = RecoveryTrigger.Manual, Actor = Person,
            Reason = "test", ScopeDescription = $"entity={entity}", TargetCount = 1,
        })).Value;
        var begun = await ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = op.Id, OwnerId = owner, Actor = Person, BodyHash = "abc", TargetEntity = entity, DlqMessageId = dlqId,
            NamespaceId = NamespaceId, EntityNameSnapshot = entity, SourceMessageIdSnapshot = messageId,
            NamespaceNameSnapshot = "orders-dev", ProviderSnapshot = CloudProviderType.Azure, EnvironmentSnapshot = EnvironmentType.Dev,
        });
        return begun.Value;
    }

    private async Task<RecoveryEntryState> StateOf(Guid entryId)
    {
        await using var fresh = NewContext();
        return (await fresh.RecoveryLedgerEntries.AsNoTracking().FirstAsync(e => e.Id == entryId)).State;
    }

    [Fact]
    public async Task A_second_attempt_on_the_same_message_is_refused_while_the_first_has_no_answer()
    {
        var ledger = Restarted();
        var first = await Begin(ledger);

        var op = (await ledger.OpenOperationAsync(new OpenRecoveryOperationRequest
        {
            OwnerId = Owner, Kind = RecoveryOperationKind.Replay, Trigger = RecoveryTrigger.AutoRule, Actor = Person,
            Reason = "again", ScopeDescription = "entity=orders", TargetCount = 1,
        })).Value;
        var second = await ledger.BeginEntryAsync(new BeginRecoveryEntryRequest
        {
            OperationId = op.Id, OwnerId = Owner, Actor = Person, BodyHash = "abc", TargetEntity = "orders", DlqMessageId = 7,
            NamespaceId = NamespaceId, EntityNameSnapshot = "orders", SourceMessageIdSnapshot = "msg-1",
        });

        second.IsFailure.Should().BeTrue("a second send could duplicate a message the first already put back");
        second.Error.Code.Should().Be(EscalationReasons.ReplayOutcomeUnknown);
        (await StateOf(first.Id)).Should().Be(RecoveryEntryState.Executing);
    }

    [Fact]
    public async Task The_same_message_is_recognised_by_the_clouds_own_id_when_the_row_differs()
    {
        var ledger = Restarted();
        await Begin(ledger, dlqId: 7, messageId: "msg-1");

        (await ledger.HasUnresolvedAttemptAsync(Owner, dlqMessageId: 999, NamespaceId, "orders", "msg-1")).Should().BeTrue();
        (await ledger.HasUnresolvedAttemptAsync(Owner, dlqMessageId: 999, NamespaceId, "orders", "another")).Should().BeFalse();
        (await ledger.HasUnresolvedAttemptAsync(Owner, 7, null, null, null)).Should().BeTrue();
        (await ledger.HasUnresolvedAttemptAsync(Owner, null, null, null, null)).Should().BeFalse("with nothing to recognise it by, nothing can be called unresolved");
    }

    [Fact]
    public async Task An_unresolved_attempt_never_blocks_another_message_or_another_owner()
    {
        var ledger = Restarted();
        await Begin(ledger);

        (await ledger.HasUnresolvedAttemptAsync(Owner, 8, NamespaceId, "orders", "msg-2")).Should().BeFalse();
        (await ledger.HasUnresolvedAttemptAsync("someone-else", 7, NamespaceId, "orders", "msg-1")).Should().BeFalse();
        (await Begin(ledger, dlqId: 8, messageId: "msg-2")).State.Should().Be(RecoveryEntryState.Executing);
    }

    [Fact]
    public async Task A_stale_executing_entry_is_settled_as_unknown_after_a_restart_and_still_blocks()
    {
        var before = Restarted();
        var entry = await Begin(before); // the process dies here: the cloud was called, the answer never recorded

        var after = Restarted();
        var settled = await after.ReconcileInterruptedAsync(DateTimeOffset.UtcNow.AddSeconds(1));

        settled.Should().Be(1);
        (await StateOf(entry.Id)).Should().Be(RecoveryEntryState.ExecutionUnknown, "not success and not failure: nobody knows");
        await using var fresh = NewContext();
        var settledEntry = await fresh.RecoveryLedgerEntries.AsNoTracking().FirstAsync(e => e.Id == entry.Id);
        settledEntry.ClosedAt.Should().BeNull("it stays open until a person resolves it");
        settledEntry.Disposition.Should().BeNull();
        (await after.HasUnresolvedAttemptAsync(Owner, 7, NamespaceId, "orders", "msg-1")).Should().BeTrue();
        (await after.VerifyChainAsync(Owner)).IsValid.Should().BeTrue("settling is written to the chain like any other event");
    }

    [Fact]
    public async Task An_entry_that_began_after_the_process_started_is_never_touched()
    {
        var ledger = Restarted();
        var processStarted = DateTimeOffset.UtcNow.AddSeconds(-5);
        var live = await Begin(ledger); // begun after "start": it can belong to a call that is still running

        (await ledger.ReconcileInterruptedAsync(processStarted)).Should().Be(0);
        (await StateOf(live.Id)).Should().Be(RecoveryEntryState.Executing);
    }

    [Fact]
    public async Task Restarting_again_changes_nothing_more()
    {
        var entry = await Begin(Restarted());
        var cutoff = DateTimeOffset.UtcNow.AddSeconds(1);

        (await Restarted().ReconcileInterruptedAsync(cutoff)).Should().Be(1);
        (await Restarted().ReconcileInterruptedAsync(cutoff.AddSeconds(30))).Should().Be(0);
        (await Restarted().ReconcileInterruptedAsync(cutoff.AddMinutes(30))).Should().Be(0);

        await using var fresh = NewContext();
        (await fresh.RecoveryEvents.CountAsync(e => e.EntryId == entry.Id && e.EventType == RecoveryEventType.ExecutionUnknown))
            .Should().Be(1, "each restart must not add another 'unknown' to the record");
        (await StateOf(entry.Id)).Should().Be(RecoveryEntryState.ExecutionUnknown);
    }

    [Fact]
    public async Task Entries_with_an_answer_are_left_exactly_as_they_were()
    {
        var ledger = Restarted();
        var observing = await Begin(ledger, 1, "a");
        var rejected = await Begin(ledger, 2, "b");
        await ledger.RecordExecutionAsync(new RecordExecutionRequest { EntryId = observing.Id, OwnerId = Owner, Actor = Person, Outcome = RecoveryExecutionOutcome.Accepted });
        await ledger.RecordExecutionAsync(new RecordExecutionRequest { EntryId = rejected.Id, OwnerId = Owner, Actor = Person, Outcome = RecoveryExecutionOutcome.Rejected });

        (await Restarted().ReconcileInterruptedAsync(DateTimeOffset.UtcNow.AddHours(1))).Should().Be(0);
        (await StateOf(observing.Id)).Should().Be(RecoveryEntryState.Observing);
        (await StateOf(rejected.Id)).Should().Be(RecoveryEntryState.ExecutionFailed);
    }

    [Fact]
    public async Task An_attempt_with_a_recorded_answer_does_not_block_a_later_one()
    {
        var ledger = Restarted();
        var first = await Begin(ledger);
        await ledger.RecordExecutionAsync(new RecordExecutionRequest { EntryId = first.Id, OwnerId = Owner, Actor = Person, Outcome = RecoveryExecutionOutcome.Rejected });

        (await ledger.HasUnresolvedAttemptAsync(Owner, 7, NamespaceId, "orders", "msg-1")).Should().BeFalse("a refusal means nothing was sent");
        (await Begin(ledger)).State.Should().Be(RecoveryEntryState.Executing);
    }

    [Fact]
    public async Task A_person_resolving_an_unknown_attempt_closes_it_without_inventing_an_outcome()
    {
        var entry = await Begin(Restarted());
        var ledger = Restarted();
        await ledger.ReconcileInterruptedAsync(DateTimeOffset.UtcNow.AddSeconds(1));

        var closed = await ledger.CloseAsync(entry.Id, Owner, Person, "Outcome was unknown; a person checked: the queue holds one copy");

        closed.IsSuccess.Should().BeTrue();
        closed.Value.State.Should().Be(RecoveryEntryState.WrittenOff);
        closed.Value.Disposition.Should().Be(RecoveryDisposition.WrittenOff, "never Recovered and never Failed");
        (await ledger.HasUnresolvedAttemptAsync(Owner, 7, NamespaceId, "orders", "msg-1")).Should().BeFalse("only now may a fresh, gated attempt be made");
        (await ledger.VerifyChainAsync(Owner)).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task The_gate_denies_every_kind_of_actor_while_an_attempt_has_no_answer()
    {
        var ledger = Restarted();
        await Begin(ledger);
        var gate = new RecoveryEligibilityGate(new EligibilityTestLedger(_db), NullLogger<RecoveryEligibilityGate>.Instance);

        foreach (var actor in new[] { RecoveryActorKind.User, RecoveryActorKind.ApiKey, RecoveryActorKind.Automation, RecoveryActorKind.System })
        {
            var decision = await gate.EvaluateAsync(new RecoveryEligibilityRequest(
                Owner, RecoveryOperationKind.Replay, actor, RecoveryTrigger.Manual, NamespaceId, "orders", "abc", null,
                EnvironmentType.Dev, Provider: CloudProviderType.Azure, DlqMessageId: 7, SourceMessageId: "msg-1"));

            decision.Verdict.Should().Be(EligibilityVerdict.Deny, $"{actor}: an approval would only repeat the attempt");
            decision.ReasonCode.Should().Be(EscalationReasons.ReplayOutcomeUnknown);
        }
    }

    [Fact]
    public async Task The_gate_lets_a_message_with_no_open_attempt_through_as_before()
    {
        var gate = new RecoveryEligibilityGate(new EligibilityTestLedger(_db), NullLogger<RecoveryEligibilityGate>.Instance);

        var decision = await gate.EvaluateAsync(new RecoveryEligibilityRequest(
            Owner, RecoveryOperationKind.Replay, RecoveryActorKind.User, RecoveryTrigger.Manual, NamespaceId, "orders", "abc", null,
            EnvironmentType.Dev, Provider: CloudProviderType.Azure, DlqMessageId: 7, SourceMessageId: "msg-1"));

        decision.Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task An_unknown_attempt_is_pending_work_a_person_can_see_and_is_not_an_approval()
    {
        var entry = await Begin(Restarted());
        await Restarted().ReconcileInterruptedAsync(DateTimeOffset.UtcNow.AddSeconds(1));
        var service = new PendingWorkService(NewContext(), new NoAgents());

        var page = await service.ListAsync(new PendingWorkScope(Owner, null), 50, CancellationToken.None);

        var item = page.Items.Should().ContainSingle(i => i.Kind == "unresolved").Subject;
        item.EntryId.Should().Be(entry.Id);
        item.ReasonCode.Should().Be(EscalationReasons.ReplayOutcomeUnknown);
        item.Reason.Should().Contain("Check the queue").And.NotContain("failed").And.NotContain("recovered");
        page.Items.Should().NotContain(i => i.Kind == "approval", "an unresolved attempt is never offered as something to approve");
    }

    [Fact]
    public async Task A_resolved_attempt_leaves_the_pending_work()
    {
        var entry = await Begin(Restarted());
        var ledger = Restarted();
        await ledger.ReconcileInterruptedAsync(DateTimeOffset.UtcNow.AddSeconds(1));
        await ledger.CloseAsync(entry.Id, Owner, Person, "Outcome was unknown; a person checked: nothing was sent");

        var page = await new PendingWorkService(NewContext(), new NoAgents()).ListAsync(new PendingWorkScope(Owner, null), 50, CancellationToken.None);

        page.Items.Should().BeEmpty();
    }

    private sealed class NoAgents : ServiceHub.Core.Interfaces.IAgentRegistry
    {
        public IReadOnlyList<ServiceHub.Core.Models.AgentRuntimeState> All() => [];
        public IReadOnlyList<ServiceHub.Core.Models.AgentDescriptor> Dormant() => [];
        public ServiceHub.Core.Models.AgentRuntimeState? StateOf(string agentId) => null;
        public bool SetPaused(string agentId, bool paused) => false;
        public IReadOnlyList<ServiceHub.Core.Models.AgentCycleRecord> RecentCycles(string agentId) => [];
    }
}
