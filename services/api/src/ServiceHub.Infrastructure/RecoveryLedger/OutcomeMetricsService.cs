using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// <inheritdoc cref="IOutcomeMetricsService"/>
/// </summary>
/// <remarks>
/// Every figure is a count, average, or duration computed directly from
/// <see cref="DlqDbContext.RecoveryLedgerEntries"/>, <see cref="DlqDbContext.RecoveryOperations"/>
/// and <see cref="DlqDbContext.RecoveryEvents"/> — the same rows <see cref="RecoveryLedgerService"/>
/// already wrote for the Recovery Evidence Ledger. No modelled, estimated, or extrapolated value
/// is ever produced; a metric this service cannot trace to a specific row is not added.
/// </remarks>
public sealed class OutcomeMetricsService : IOutcomeMetricsService
{
    private static readonly TimeSpan DefaultWindow = TimeSpan.FromDays(7);

    private readonly DlqDbContext _dbContext;

    /// <summary>Initialises a new instance of <see cref="OutcomeMetricsService"/>.</summary>
    public OutcomeMetricsService(DlqDbContext dbContext)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
    }

    /// <inheritdoc />
    public async Task<OutcomeMetricsOverview> GetOverviewAsync(
        string ownerId,
        TimeSpan? window = null,
        CloudProviderType? provider = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner identifier is required.", nameof(ownerId));
        }

        var windowEnd = DateTimeOffset.UtcNow;
        var windowStart = windowEnd - (window ?? DefaultWindow);

        var recoveredEntries = await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId
                && e.Disposition == RecoveryDisposition.Recovered
                && e.ClosedAt != null
                && e.ClosedAt >= windowStart && e.ClosedAt <= windowEnd
                && (provider == null || e.ProviderSnapshot == provider))
            .Select(e => new { e.BegunAt, e.ClosedAt, e.OperationId })
            .ToListAsync(cancellationToken);

        var messagesAbandoned = await _dbContext.RecoveryLedgerEntries
            .AsNoTracking()
            .CountAsync(e => e.OwnerId == ownerId
                && e.Disposition == RecoveryDisposition.WrittenOff
                && e.ClosedAt != null
                && e.ClosedAt >= windowStart && e.ClosedAt <= windowEnd
                && (provider == null || e.ProviderSnapshot == provider), cancellationToken);

        // EligibilityDeclined events are written with EntryId set to the entry created alongside
        // them (see RecoveryLedgerService), so a provider filter joins through it rather than
        // needing its own denormalised provider column.
        var gateRefusals = provider == null
            ? await _dbContext.RecoveryEvents
                .AsNoTracking()
                .CountAsync(e => e.OwnerId == ownerId
                    && e.EventType == RecoveryEventType.EligibilityDeclined
                    && e.OccurredAt >= windowStart && e.OccurredAt <= windowEnd, cancellationToken)
            : await _dbContext.RecoveryEvents
                .AsNoTracking()
                .Where(e => e.OwnerId == ownerId
                    && e.EventType == RecoveryEventType.EligibilityDeclined
                    && e.OccurredAt >= windowStart && e.OccurredAt <= windowEnd
                    && e.EntryId != null)
                .Join(_dbContext.RecoveryLedgerEntries.AsNoTracking(),
                    e => e.EntryId, entry => entry.Id, (e, entry) => entry.ProviderSnapshot)
                .CountAsync(providerSnapshot => providerSnapshot == provider, cancellationToken);

        double? medianSeconds = null;
        var autonomousRecoveries = 0;

        if (recoveredEntries.Count > 0)
        {
            var seconds = recoveredEntries
                .Select(e => (e.ClosedAt!.Value - e.BegunAt).TotalSeconds)
                .OrderBy(s => s)
                .ToList();
            medianSeconds = ComputeMedian(seconds);

            var operationIds = recoveredEntries.Select(e => e.OperationId).Distinct().ToList();
            var autonomousOperationIds = await _dbContext.RecoveryOperations
                .AsNoTracking()
                .Where(o => operationIds.Contains(o.Id)
                    && (o.ActorKind == RecoveryActorKind.Automation || o.ActorKind == RecoveryActorKind.System))
                .Select(o => o.Id)
                .ToListAsync(cancellationToken);
            var autonomousSet = autonomousOperationIds.ToHashSet();
            autonomousRecoveries = recoveredEntries.Count(e => autonomousSet.Contains(e.OperationId));
        }

        return new OutcomeMetricsOverview(
            GeneratedAt: DateTimeOffset.UtcNow,
            WindowStartUtc: windowStart,
            WindowEndUtc: windowEnd,
            MessagesRecovered: recoveredEntries.Count,
            MessagesAbandoned: messagesAbandoned,
            MedianSecondsToVerifiedRecovery: medianSeconds,
            AutonomousRecoveries: autonomousRecoveries,
            GateRefusals: gateRefusals);
    }

    private static double ComputeMedian(IReadOnlyList<double> sortedAscending)
    {
        var count = sortedAscending.Count;
        var mid = count / 2;
        return count % 2 == 0
            ? (sortedAscending[mid - 1] + sortedAscending[mid]) / 2.0
            : sortedAscending[mid];
    }
}
