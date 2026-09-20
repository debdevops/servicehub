using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// <inheritdoc cref="IAutonomyDashboardService"/>
/// </summary>
/// <remarks>
/// Reads <c>AutonomyGrants</c> and recent transition events via <see cref="IRecoveryLedger"/>
/// (the ledger's own read surface), and <c>AutoReplayRules</c> directly via
/// <see cref="DlqDbContext"/> for circuit-breaker trips — mirroring
/// <see cref="ApprovalQueueService"/>'s mixed dependency style. No schema change, no new trust
/// computation (roadmap principle honoured verbatim by the roadmap entry itself): every number
/// here is a count, filter, or grouping over rows <see cref="RecoveryLedgerService"/> and
/// <c>AutoReplayExecutor</c>/<c>AutoReplayRuleCircuitBreaker</c> logic already wrote.
/// </remarks>
public sealed class AutonomyDashboardService : IAutonomyDashboardService
{
    private const int RecentTransitionsLimit = 20;
    private const string CircuitBreakerDisabledReason = "CircuitBreaker";

    private readonly IRecoveryLedger _recoveryLedger;
    private readonly DlqDbContext _dbContext;

    /// <summary>Initialises a new instance of <see cref="AutonomyDashboardService"/>.</summary>
    public AutonomyDashboardService(IRecoveryLedger recoveryLedger, DlqDbContext dbContext)
    {
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<AutonomyDashboardOverview> GetOverviewAsync(
        string ownerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner identifier is required.", nameof(ownerId));
        }

        var grants = await _recoveryLedger.GetAutonomyGrantsAsync(ownerId, cancellationToken);
        var emergencyStopActive = await _recoveryLedger.IsEmergencyStopActiveAsync(ownerId, cancellationToken);
        var recentTransitions = await _recoveryLedger.GetRecentAutonomyTransitionsAsync(
            ownerId, RecentTransitionsLimit, cancellationToken);

        var trippedRules = await _dbContext.AutoReplayRules
            .AsNoTracking()
            .Where(r => r.OwnerId == ownerId && r.DisabledReason == CircuitBreakerDisabledReason)
            .OrderByDescending(r => r.UpdatedAt)
            .Select(r => new CircuitBreakerTrip(r.Id, r.Name, r.DisabledReasonDetail))
            .ToListAsync(cancellationToken);

        var levelCounts = grants
            .GroupBy(g => (g.ActionKind, g.CurrentLevel))
            .Select(group => new AutonomyLevelCount(
                ActionKind: group.Key.ActionKind.ToString(),
                Level: (int)group.Key.CurrentLevel,
                LevelLabel: FormatLevelLabel(group.Key.CurrentLevel),
                Count: group.Count()))
            .OrderBy(c => c.ActionKind, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.Level)
            .ToList();

        var grantSummaries = grants
            .OrderByDescending(g => g.UpdatedAtUtc)
            .Select(g => new AutonomyGrantSummary(
                SignatureHash: g.SignatureHash,
                ActionKind: g.ActionKind.ToString(),
                CurrentLevel: (int)g.CurrentLevel,
                LevelLabel: FormatLevelLabel(g.CurrentLevel),
                UpdatedAtUtc: g.UpdatedAtUtc))
            .ToList();

        var transitionSummaries = recentTransitions
            .Select(t => new AutonomyTransitionSummary(
                SignatureHash: t.SignatureHash,
                ActionKind: t.ActionKind.ToString(),
                PreviousLevel: (int)t.PreviousLevel,
                NewLevel: (int)t.NewLevel,
                Reason: t.Reason,
                OccurredAtUtc: t.OccurredAtUtc))
            .ToList();

        return new AutonomyDashboardOverview(
            GeneratedAt: DateTimeOffset.UtcNow,
            EmergencyStopActive: emergencyStopActive,
            TotalSignatures: grants.Select(g => g.SignatureHash).Distinct().Count(),
            LevelCounts: levelCounts,
            Grants: grantSummaries,
            CircuitBreakerTrips: trippedRules,
            RecentTransitions: transitionSummaries);
    }

    private static string FormatLevelLabel(AutonomyLevel level) => $"{level} (L{(int)level})";
}
