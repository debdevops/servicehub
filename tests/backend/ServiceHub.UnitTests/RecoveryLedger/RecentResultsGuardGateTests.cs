using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.RecoveryLedger;

/// <summary>
/// Design 10 §8: automation that the real gate would allow is handed to a person when the signature's latest verified
/// results are mixed. It wraps the gate and can only make a decision stricter.
/// </summary>
public sealed class RecentResultsGuardGateTests : IDisposable
{
    private const string Owner = "owner1";
    private const string Sig = "sig-a";
    private static readonly DateTimeOffset T0 = new(2026, 10, 8, 9, 0, 0, TimeSpan.Zero);

    private readonly ServiceHubDbContext _db;
    private readonly Mock<IRecoveryEligibilityGate> _inner = new();
    private readonly RecentResultsGuardGate _sut;
    private int _minute;

    public RecentResultsGuardGateTests()
    {
        _db = new ServiceHubDbContext(new DbContextOptionsBuilder<ServiceHubDbContext>().UseSqlite("DataSource=:memory:").Options);
        _db.Database.OpenConnection();
        _db.Database.EnsureCreated();
        _inner.Setup(g => g.EvaluateAsync(It.IsAny<RecoveryEligibilityRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(EligibilityDecision.Allow);
        _sut = new RecentResultsGuardGate(_inner.Object, _db, NullLogger<RecentResultsGuardGate>.Instance);
    }

    public void Dispose()
    {
        _db.Database.CloseConnection();
        _db.Dispose();
    }

    private static RecoveryEligibilityRequest Request(RecoveryActorKind actor = RecoveryActorKind.Automation, string? sig = Sig,
        RecoveryOperationKind kind = RecoveryOperationKind.Replay) =>
        new(Owner, kind, actor, RecoveryTrigger.AutoRule, Guid.NewGuid(), "orders", "body", sig, EnvironmentType.Dev, Provider: CloudProviderType.Azure);

    /// <summary>Seeds one closed result, oldest first.</summary>
    private void Result(RecoveryDisposition disposition, string sig = Sig, RecoveryOperationKind kind = RecoveryOperationKind.Replay, string owner = Owner, bool closed = true)
    {
        var at = T0.AddMinutes(_minute++);
        var op = new RecoveryOperation
        {
            OwnerId = owner, Kind = kind, Trigger = RecoveryTrigger.AutoRule, ActorIdentity = "rule", ActorKind = RecoveryActorKind.Automation,
            ScopeDescription = "t", ServiceVersion = "t", OpenedAt = at, TargetCount = 1,
        };
        _db.RecoveryOperations.Add(op);
        _db.RecoveryLedgerEntries.Add(new RecoveryLedgerEntry
        {
            OperationId = op.Id, OwnerId = owner, BodyHash = Guid.NewGuid().ToString("N"), TargetEntity = "orders", BegunAt = at,
            SignatureHashSnapshot = sig, Disposition = disposition, ClosedAt = closed ? at : null,
        });
        _db.SaveChanges();
    }

    private void Results(RecoveryDisposition d, int n) { for (var i = 0; i < n; i++) Result(d); }

    [Fact]
    public async Task A_clean_recent_run_is_allowed_through()
    {
        Results(RecoveryDisposition.Recovered, 12);
        (await _sut.EvaluateAsync(Request())).Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task One_bad_result_in_the_window_is_tolerated()
    {
        Results(RecoveryDisposition.Recovered, 5);
        Result(RecoveryDisposition.Returned);
        Results(RecoveryDisposition.Recovered, 3);
        (await _sut.EvaluateAsync(Request())).Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Theory]
    [InlineData(RecoveryDisposition.Returned)]
    [InlineData(RecoveryDisposition.Failed)]
    public async Task Two_bad_results_in_the_window_hand_automation_to_a_person_even_after_a_long_good_history(RecoveryDisposition bad)
    {
        Results(RecoveryDisposition.Recovered, 420);   // all-time 99%+ — what the scorer sees
        Results(bad, 2);
        var decision = await _sut.EvaluateAsync(Request());
        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be(RecentResultsGuardGate.ReasonRecentResultsMixed);
    }

    [Fact]
    public async Task Old_bad_results_outside_the_window_do_not_count()
    {
        Results(RecoveryDisposition.Returned, 5);
        Results(RecoveryDisposition.Recovered, RecentResultsGuardGate.WindowSize);
        (await _sut.EvaluateAsync(Request())).Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task Automation_resumes_by_itself_once_newer_results_hold()
    {
        Results(RecoveryDisposition.Returned, 3);
        (await _sut.EvaluateAsync(Request())).Verdict.Should().Be(EligibilityVerdict.Escalate);
        Results(RecoveryDisposition.Recovered, RecentResultsGuardGate.WindowSize);
        (await _sut.EvaluateAsync(Request())).Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task Unverified_declined_and_still_open_entries_are_not_evidence()
    {
        Results(RecoveryDisposition.Recovered, 3);
        Results(RecoveryDisposition.Unverified, 5);
        Results(RecoveryDisposition.Declined, 5);
        Result(RecoveryDisposition.Returned, closed: false);
        Result(RecoveryDisposition.Returned, closed: false);
        (await _sut.EvaluateAsync(Request())).Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task Other_signatures_other_owners_and_other_action_kinds_are_not_counted()
    {
        Results(RecoveryDisposition.Recovered, 3);
        Result(RecoveryDisposition.Returned, sig: "sig-b");
        Result(RecoveryDisposition.Returned, sig: "sig-b");
        Result(RecoveryDisposition.Returned, owner: "someone-else");
        Result(RecoveryDisposition.Returned, owner: "someone-else");
        Result(RecoveryDisposition.Failed, kind: RecoveryOperationKind.Purge);
        Result(RecoveryDisposition.Failed, kind: RecoveryOperationKind.Purge);
        (await _sut.EvaluateAsync(Request())).Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Theory]
    [InlineData(RecoveryActorKind.User)]
    [InlineData(RecoveryActorKind.ApiKey)]
    public async Task A_person_is_never_held_by_it(RecoveryActorKind actor)
    {
        Results(RecoveryDisposition.Returned, 5);
        (await _sut.EvaluateAsync(Request(actor))).Verdict.Should().Be(EligibilityVerdict.Allow);
    }

    [Fact]
    public async Task It_never_loosens_the_real_gate()
    {
        var held = new EligibilityDecision(EligibilityVerdict.Escalate, "AUTONOMY_GRANT_INSUFFICIENT");
        _inner.Setup(g => g.EvaluateAsync(It.IsAny<RecoveryEligibilityRequest>(), It.IsAny<CancellationToken>())).ReturnsAsync(held);
        Results(RecoveryDisposition.Recovered, 10);
        (await _sut.EvaluateAsync(Request())).Should().Be(held);
    }

    [Fact]
    public async Task A_request_with_no_signature_is_left_to_the_real_gate()
    {
        var decision = await _sut.EvaluateAsync(Request(sig: null));
        decision.Should().Be(EligibilityDecision.Allow);
    }

    [Fact]
    public async Task An_unreadable_ledger_stops_automation_rather_than_letting_it_through()
    {
        _db.Database.CloseConnection();
        _db.Database.EnsureDeleted();   // the tables are gone: the query throws
        var decision = await _sut.EvaluateAsync(Request());
        decision.Verdict.Should().Be(EligibilityVerdict.Escalate);
        decision.ReasonCode.Should().Be(RecentResultsGuardGate.ReasonRecentResultsQueryError);
    }
}
