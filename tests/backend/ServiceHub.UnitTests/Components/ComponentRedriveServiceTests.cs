using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Components;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Results;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.Recovery;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Components;

/// <summary>
/// The gated redrive (plan PP5) with the real gate and the real ledger on a migrated database, and a fake cloud. Proves the order
/// (gate, record, cloud, record), who may start it, and that the answer is never more than what the cloud accepted.
/// </summary>
public sealed class ComponentRedriveServiceTests : IDisposable
{
    private const string Run = "arn:aws:states:us-east-1:123456789012:execution:orders:run-1";
    private const string Machine = "arn:aws:states:us-east-1:123456789012:stateMachine:orders";
    private static readonly RecoveryActor Person = new("session", RecoveryActorKind.User);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly ServiceHubDbContext _db;
    private readonly RecoveryLedgerService _ledger;
    private readonly FakeMover _mover = new();
    private readonly Mock<IAuditTrail> _audit = new();
    private readonly Namespace _ns = Namespace.Create(
        "sqs.us-east-1.amazonaws.com", "AKIAIOSFODNN7EXAMPLE:wJalrXUtnFEMI/K7MDENG/bPxRfiCYEXAMPLEKEY",
        provider: CloudProviderType.Aws, awsRegion: "us-east-1").Value;

    private RecoverabilityClass? _class = RecoverabilityClass.R3CloudRedriveOnly;

    public ComponentRedriveServiceTests()
    {
        _connection.Open();
        _db = new ServiceHubDbContext(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite(_connection).Options);
        _db.Database.Migrate();
        _ledger = new RecoveryLedgerService(_db);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private ComponentRedriveService Service() =>
        new(_ledger, new RecoveryEligibilityGate(_ledger, NullLogger<RecoveryEligibilityGate>.Instance), [_mover], _audit.Object,
            NullLogger<ComponentRedriveService>.Instance, _ => _class);

    private Task<Result<RedriveOutcome>> Redrive(RecoveryActor? actor = null, string item = Run) =>
        Service().RequestRedriveAsync(_ns, "step-functions", item, actor ?? Person, "redrive-execution", "trace-1", default);

    [Fact]
    public async Task An_accepted_redrive_is_recorded_before_and_after_and_ends_unverified()
    {
        var outcome = (await Redrive()).Value;

        outcome.Result.Should().Be("accepted");
        outcome.State.Should().Be("Unverified");
        outcome.Message.Should().Be("AWS accepted the redrive request. ServiceHub cannot confirm the run finished fixed.");
        _mover.Asked.Should().ContainSingle().Which.Should().Be((Run, outcome.EntryId.ToString("N")), "the entry's id is the cloud's idempotency token");
        var entry = (await _ledger.GetEntryAsync(outcome.EntryId, _ns.OwnerId))!;
        entry.Disposition.Should().Be(RecoveryDisposition.Unverified);
        entry.EntityNameSnapshot.Should().Be(Machine);
        entry.SourceMessageIdSnapshot.Should().Be(Run);
        (await _ledger.GetOperationAsync(outcome.OperationId, _ns.OwnerId))!.Kind.Should().Be(RecoveryOperationKind.Redrive);
        _audit.Verify(a => a.RecordAsync(It.Is<AuditLog>(l => l.Action == "Redrive.Execution" && l.ResourceName == Run), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task The_words_name_the_connections_cloud_and_the_movers_verb()
    {
        const string logicRun = "/subscriptions/11111111-1111-1111-1111-111111111111/resourceGroups/rg/providers/Microsoft.Logic/workflows/orders/runs/run-1";
        var azure = Namespace.CreateWithManagedIdentity("azure-words", ConnectionAuthType.ManagedIdentity, provider: CloudProviderType.Azure).Value;
        var mover = new FakeMover { ComponentKey = "logic-apps", Provider = CloudProviderType.Azure, Verb = "resubmit", OwnedPrefix = "/subscriptions/" };
        var service = new ComponentRedriveService(
            _ledger, new RecoveryEligibilityGate(_ledger, NullLogger<RecoveryEligibilityGate>.Instance), [mover], _audit.Object, NullLogger<ComponentRedriveService>.Instance, _ => _class);

        var check = (await service.CheckAsync(azure, "logic-apps", logicRun, Person, default)).Value;
        var outcome = (await service.RequestRedriveAsync(azure, "logic-apps", logicRun, Person, "redrive-execution", "trace-1", default)).Value;

        check.Verb.Should().Be("resubmit");
        check.Message.Should().Be("A person can ask Azure to resubmit this run.");
        outcome.Message.Should().Be("Azure accepted the resubmit request. ServiceHub cannot confirm the run finished fixed.");
        outcome.Message.Should().NotContain("AWS").And.NotContain("redrive");
    }

    [Fact]
    public async Task A_refusal_uses_the_movers_verb()
    {
        _class = RecoverabilityClass.R0Unknown;
        var service = new ComponentRedriveService(
            _ledger, new RecoveryEligibilityGate(_ledger, NullLogger<RecoveryEligibilityGate>.Instance), [new FakeMover { Verb = "resubmit" }], _audit.Object,
            NullLogger<ComponentRedriveService>.Instance, _ => _class);

        var check = (await service.CheckAsync(_ns, "step-functions", Run, Person, default)).Value;

        check.CanRedrive.Should().BeFalse();
        check.Message.Should().Contain("resubmit").And.NotContain("redrive");
    }

    [Fact]
    public async Task The_check_says_when_the_cloud_last_accepted_a_request_for_the_same_run_and_never_blocks_a_second()
    {
        (await Service().CheckAsync(_ns, "step-functions", Run, Person, default)).Value.PreviousRequestAtUtc.Should().BeNull("nothing was requested yet");

        var first = (await Redrive()).Value;
        var check = (await Service().CheckAsync(_ns, "step-functions", Run, Person, default)).Value;
        var other = (await Service().CheckAsync(_ns, "step-functions", Run.Replace("run-1", "run-2", StringComparison.Ordinal), Person, default)).Value;

        first.Result.Should().Be("accepted");
        check.CanRedrive.Should().BeTrue("a warning, never a block: a person may genuinely want a second run");
        check.PreviousRequestAtUtc.Should().NotBeNull();
        other.PreviousRequestAtUtc.Should().BeNull("another run was never requested");
        (await Redrive()).Value.Result.Should().Be("accepted");
    }

    [Fact]
    public async Task The_ledger_entry_exists_in_the_executing_state_when_the_cloud_is_called()
    {
        var seen = (RecoveryEntryState?)null;
        _mover.OnCall = () => seen = _db.RecoveryLedgerEntries.AsNoTracking().Single().State;

        await Redrive();

        seen.Should().Be(RecoveryEntryState.Executing, "the attempt is on the record before the cloud is touched");
    }

    [Theory]
    [InlineData(RecoveryActorKind.Automation)]
    [InlineData(RecoveryActorKind.System)]
    public async Task An_agent_or_a_rule_is_refused_before_anything_is_recorded_or_sent(RecoveryActorKind kind)
    {
        var result = await Redrive(new RecoveryActor("agent", kind));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(RecoveryEligibilityGate.ReasonRedriveAutomationProhibited);
        _mover.Asked.Should().BeEmpty();
        _db.RecoveryOperations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_component_whose_class_does_not_offer_a_redrive_is_refused()
    {
        _class = RecoverabilityClass.R0Unknown;

        (await Redrive()).Error.Code.Should().Be(RecoveryEligibilityGate.ReasonRedriveNotOffered);
        _mover.Asked.Should().BeEmpty();
        _db.RecoveryOperations.Should().BeEmpty();
    }

    [Fact]
    public void Only_step_functions_offers_a_redrive_in_the_real_catalogue_until_another_live_run_is_recorded()
    {
        // Workflows names a Resubmit path too, but it is Being validated, so its class stays R0 and the gate denies it.
        ComponentCatalog.All.Where(c => c.Class.OffersCloudRedrive()).Select(c => c.ComponentKey).Should().Equal("step-functions");
    }

    [Fact]
    public async Task A_run_that_is_not_in_this_connections_account_and_region_is_refused_before_anything_is_recorded()
    {
        var result = await Redrive(item: "arn:aws:states:eu-west-1:123456789012:execution:orders:run-1");

        result.IsFailure.Should().BeTrue();
        _mover.Asked.Should().BeEmpty();
        _db.RecoveryOperations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_second_request_after_a_lost_answer_is_refused_until_a_person_resolves_the_first()
    {
        _mover.Answer = Result.Failure<MoveOnAcceptance>(Error.ExternalService("AWS.StepFunctions.RedriveFailed", "timeout"));
        var first = (await Redrive()).Value;
        first.Result.Should().Be("unknown");
        first.Message.Should().Contain("Check the run before asking again");

        var second = await Redrive();

        second.IsFailure.Should().BeTrue();
        _mover.Asked.Should().HaveCount(1, "a second send could redrive twice");
    }

    [Fact]
    public async Task An_exception_from_the_cloud_is_recorded_as_unknown_never_as_failed()
    {
        _mover.Throw = new InvalidOperationException("boom");

        var outcome = (await Redrive()).Value;

        outcome.Result.Should().Be("unknown");
        (await _ledger.GetEntryAsync(outcome.EntryId, _ns.OwnerId))!.State.Should().Be(RecoveryEntryState.ExecutionUnknown);
    }

    [Theory]
    [InlineData(ErrorType.Conflict)]
    [InlineData(ErrorType.Forbidden)]
    [InlineData(ErrorType.NotFound)]
    public async Task A_refusal_that_cannot_have_reached_the_cloud_is_rejected_and_the_run_can_be_asked_again(ErrorType type)
    {
        _mover.Answer = Result.Failure<MoveOnAcceptance>(new Error("AWS.X", "AWS said no", type));

        var outcome = (await Redrive()).Value;

        outcome.Result.Should().Be("rejected");
        outcome.ErrorCode.Should().Be("AWS.X");
        (await _ledger.GetEntryAsync(outcome.EntryId, _ns.OwnerId))!.State.Should().Be(RecoveryEntryState.ExecutionFailed);
        _mover.Answer = Result.Success(new MoveOnAcceptance(null));
        (await Redrive()).Value.Result.Should().Be("accepted");
    }

    [Fact]
    public async Task A_connection_marked_read_only_is_refused()
    {
        _ns.DeclareCanSend(false);

        var result = await Redrive();

        result.Error.Code.Should().Be(RecoveryEligibilityGate.ReasonConnectionReadOnly);
        _mover.Asked.Should().BeEmpty();
    }

    [Fact]
    public async Task The_check_says_yes_or_why_not_and_records_nothing()
    {
        var yes = (await Service().CheckAsync(_ns, "step-functions", Run, Person, default)).Value;
        _class = RecoverabilityClass.R0Unknown;
        var no = (await Service().CheckAsync(_ns, "step-functions", Run, Person, default)).Value;

        yes.CanRedrive.Should().BeTrue();
        no.CanRedrive.Should().BeFalse();
        no.ReasonCode.Should().Be(RecoveryEligibilityGate.ReasonRedriveNotOffered);
        _mover.Asked.Should().BeEmpty();
        _db.RecoveryOperations.Should().BeEmpty();
    }

    [Fact]
    public async Task A_component_with_no_mover_on_this_connection_is_not_found()
    {
        var result = await Service().RequestRedriveAsync(_ns, "workflows", Run, Person, null, null, default);

        result.IsFailure.Should().BeTrue();
        result.Error.Type.Should().Be(ErrorType.NotFound);
    }

    private sealed class FakeMover : IComponentMoveOn
    {
        public List<(string Item, string Token)> Asked { get; } = [];

        public Result<MoveOnAcceptance> Answer { get; set; } = Result.Success(new MoveOnAcceptance(null));

        public Exception? Throw { get; set; }

        public Action? OnCall { get; set; }

        public string ComponentKey { get; init; } = "step-functions";

        public CloudProviderType Provider { get; init; } = CloudProviderType.Aws;

        public string Verb { get; init; } = "redrive";

        public string OwnedPrefix { get; init; } = "arn:aws:states:us-east-1:";

        public bool Owns(Namespace ns, string itemId) => itemId.StartsWith(OwnedPrefix, StringComparison.Ordinal);

        public string PlaceOf(string itemId) => Machine;

        public Task<Result<MoveOnAcceptance>> RedriveAsync(Namespace ns, string itemId, string requestToken, CancellationToken ct)
        {
            Asked.Add((itemId, requestToken));
            OnCall?.Invoke();
            return Throw is not null ? throw Throw : Task.FromResult(Answer);
        }
    }
}
