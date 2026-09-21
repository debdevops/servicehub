using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Infrastructure.Persistence;
using ServiceHub.Infrastructure.RecoveryLedger;

namespace ServiceHub.UnitTests.Infrastructure.RecoveryLedger;

public sealed class AutonomyDashboardServiceTests : IDisposable
{
    private const string OwnerA = "owner-a";
    private const string OwnerB = "owner-b";

    private readonly DlqDbContext _dbContext;
    private readonly RecoveryLedgerService _ledger;
    private readonly AutonomyDashboardService _sut;

    public AutonomyDashboardServiceTests()
    {
        var options = new DbContextOptionsBuilder<DlqDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        _dbContext = new DlqDbContext(options);
        _dbContext.Database.OpenConnection();
        _dbContext.Database.EnsureCreated();

        _ledger = new RecoveryLedgerService(_dbContext);
        _sut = new AutonomyDashboardService(_ledger, _dbContext);
    }

    public void Dispose()
    {
        _dbContext.Database.CloseConnection();
        _dbContext.Dispose();
    }

    private async Task<AutoReplayRule> AddRuleAsync(
        string name, string ownerId, string? disabledReason = null, string? disabledReasonDetail = null)
    {
        var rule = new AutoReplayRule
        {
            Name = name,
            OwnerId = ownerId,
            ConditionsJson = "[]",
            ActionsJson = "[]",
            CreatedAt = DateTimeOffset.UtcNow,
            Enabled = disabledReason is null,
            DisabledReason = disabledReason,
            DisabledReasonDetail = disabledReasonDetail,
        };
        _dbContext.AutoReplayRules.Add(rule);
        await _dbContext.SaveChangesAsync();
        return rule;
    }

    [Fact]
    public async Task Constructor_NullRecoveryLedger_Throws()
    {
        var act = () => new AutonomyDashboardService(null!, _dbContext);
        act.Should().Throw<ArgumentNullException>().WithParameterName("recoveryLedger");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Constructor_NullDbContext_Throws()
    {
        var act = () => new AutonomyDashboardService(_ledger, null!);
        act.Should().Throw<ArgumentNullException>().WithParameterName("dbContext");
        await Task.CompletedTask;
    }

    [Fact]
    public async Task GetOverviewAsync_EmptyOwnerId_Throws()
    {
        var act = () => _sut.GetOverviewAsync(" ");
        await act.Should().ThrowAsync<ArgumentException>().WithParameterName("ownerId");
    }

    [Fact]
    public async Task GetOverviewAsync_NoDataForOwner_ReturnsEmptySnapshot()
    {
        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.EmergencyStopActive.Should().BeFalse();
        overview.TotalSignatures.Should().Be(0);
        overview.LevelCounts.Should().BeEmpty();
        overview.Grants.Should().BeEmpty();
        overview.CircuitBreakerTrips.Should().BeEmpty();
        overview.RecentTransitions.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverviewAsync_GroupsGrantsByActionKindAndLevel()
    {
        await _ledger.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-1", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "earned it", null);
        await _ledger.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-2", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "earned it too", null);
        await _ledger.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-3", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Unattended, "top trust", null);

        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.TotalSignatures.Should().Be(3);
        overview.LevelCounts.Should().HaveCount(2);
        overview.LevelCounts.Should().ContainSingle(c => c.Level == (int)AutonomyLevel.Standing && c.Count == 2);
        overview.LevelCounts.Should().ContainSingle(c => c.Level == (int)AutonomyLevel.Unattended && c.Count == 1);
        overview.Grants.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetOverviewAsync_ScopesToCallerOwner()
    {
        await _ledger.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-a", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "owner A", null);
        await _ledger.RecordAutonomyGrantTransitionAsync(
            OwnerB, "sig-b", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "owner B", null);

        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.Grants.Should().ContainSingle().Which.SignatureHash.Should().Be("sig-a");
    }

    [Fact]
    public async Task GetOverviewAsync_EmergencyStopActive_ReflectsLedgerState()
    {
        await _ledger.RecordEmergencyControlEventAsync(
            OwnerA, new ServiceHub.Core.Models.RecoveryActor("operator", RecoveryActorKind.User), activate: true, "incident");

        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.EmergencyStopActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetOverviewAsync_ExcludesManuallyDisabledRulesFromCircuitBreakerTrips()
    {
        await AddRuleAsync("Manually off", OwnerA, disabledReason: "Manual");
        await AddRuleAsync("Still on", OwnerA);

        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.CircuitBreakerTrips.Should().BeEmpty();
    }

    [Fact]
    public async Task GetOverviewAsync_IncludesCircuitBreakerTrippedRulesForOwner()
    {
        await AddRuleAsync(
            "Flaky replay rule", OwnerA,
            disabledReason: "CircuitBreaker", disabledReasonDetail: "3/20 verified successes (15%)");
        await AddRuleAsync("Other owner's tripped rule", OwnerB, disabledReason: "CircuitBreaker");

        var overview = await _sut.GetOverviewAsync(OwnerA);

        var trip = overview.CircuitBreakerTrips.Should().ContainSingle().Subject;
        trip.RuleName.Should().Be("Flaky replay rule");
        trip.DisabledReasonDetail.Should().Be("3/20 verified successes (15%)");
    }

    [Fact]
    public async Task GetOverviewAsync_IncludesRecentTransitionsNewestFirst()
    {
        await _ledger.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-t", RecoveryOperationKind.Replay,
            AutonomyLevel.Approve, AutonomyLevel.Standing, "first", null);
        await _ledger.RecordAutonomyGrantTransitionAsync(
            OwnerA, "sig-t", RecoveryOperationKind.Replay,
            AutonomyLevel.Standing, AutonomyLevel.Approve, "demoted", null);

        var overview = await _sut.GetOverviewAsync(OwnerA);

        overview.RecentTransitions.Should().HaveCount(2);
        overview.RecentTransitions[0].Reason.Should().Be("demoted");
    }
}
