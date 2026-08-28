namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Fleet-wide autonomy operations view (roadmap §11 item 5, §15 item 9) — "how much unattended
/// trust has the fleet actually earned, and is anything currently constraining it?" Pure
/// read-side aggregation over data three existing tables already hold
/// (<c>AutonomyGrants</c>, <c>AutoReplayRules</c>, <c>RecoveryEvents</c>) — no new schema, no new
/// trust computation. Where <see cref="IFleetOverviewService"/> answers "what died overnight?",
/// this answers "how autonomous is the fleet right now, and why?"
/// </summary>
public interface IAutonomyDashboardService
{
    /// <summary>
    /// Builds a fleet-wide autonomy overview for the given owner.
    /// </summary>
    /// <param name="ownerId">Tenant/owner identifier for isolation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<AutonomyDashboardOverview> GetOverviewAsync(
        string ownerId,
        CancellationToken cancellationToken = default);
}

/// <summary>Fleet-wide autonomy snapshot across every signature an owner has ever promoted.</summary>
public sealed record AutonomyDashboardOverview(
    DateTimeOffset GeneratedAt,
    bool EmergencyStopActive,
    int TotalSignatures,
    IReadOnlyList<AutonomyLevelCount> LevelCounts,
    IReadOnlyList<AutonomyGrantSummary> Grants,
    IReadOnlyList<CircuitBreakerTrip> CircuitBreakerTrips,
    IReadOnlyList<AutonomyTransitionSummary> RecentTransitions);

/// <summary>Count of signatures currently standing at one autonomy level, for one action kind.</summary>
public sealed record AutonomyLevelCount(
    string ActionKind,
    int Level,
    string LevelLabel,
    int Count);

/// <summary>One signature's current autonomy standing, for the fleet-wide grant list.</summary>
public sealed record AutonomyGrantSummary(
    string SignatureHash,
    string ActionKind,
    int CurrentLevel,
    string LevelLabel,
    DateTimeOffset UpdatedAtUtc);

/// <summary>One <see cref="Entities.AutoReplayRule"/> currently disabled by the success-rate
/// circuit breaker — never a manual disable, which carries a different <c>DisabledReason</c>.</summary>
public sealed record CircuitBreakerTrip(
    long RuleId,
    string RuleName,
    string? DisabledReasonDetail);

/// <summary>One recorded promotion or demotion, for the dashboard's recent-activity feed.</summary>
public sealed record AutonomyTransitionSummary(
    string SignatureHash,
    string ActionKind,
    int PreviousLevel,
    int NewLevel,
    string Reason,
    DateTimeOffset OccurredAtUtc);
