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
    public async Task A_component_whose_class_does_not_offer_a_redrive_is_refused_and_the_real_catalogue_offers_none_yet()
    {
        _class = RecoverabilityClass.R0Unknown;
        (await Redrive()).Error.Code.Should().Be(RecoveryEligibilityGate.ReasonRedriveNotOffered);

        var real = new ComponentRedriveService(
            _ledger, new RecoveryEligibilityGate(_ledger, NullLogger<RecoveryEligibilityGate>.Instance), [_mover], _audit.Object, NullLogger<ComponentRedriveService>.Instance);
        var result = await real.RequestRedriveAsync(_ns, "step-functions", Run, Person, null, null, default);

        result.Error.Code.Should().Be(RecoveryEligibilityGate.ReasonRedriveNotOffered, "Step Functions is Being validated until a live run is recorded");
        _mover.Asked.Should().BeEmpty();
        _db.RecoveryOperations.Should().BeEmpty();
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

        public string ComponentKey => "step-functions";

        public CloudProviderType Provider => CloudProviderType.Aws;

        public bool Owns(Namespace ns, string itemId) => itemId.StartsWith("arn:aws:states:us-east-1:", StringComparison.Ordinal);

        public string PlaceOf(string itemId) => Machine;

        public Task<Result<MoveOnAcceptance>> RedriveAsync(Namespace ns, string itemId, string requestToken, CancellationToken ct)
        {
            Asked.Add((itemId, requestToken));
            OnCall?.Invoke();
            return Throw is not null ? throw Throw : Task.FromResult(Answer);
        }
    }
}
