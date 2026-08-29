using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Counterfactual backtesting (roadmap §11 item 14) — pure read-side scoring of whether a
/// proposal a human already dispositioned in the Playbook Ledger was corroborated by what
/// actually happened afterward in the Recovery Evidence Ledger, for the same
/// (namespace, entity) the proposal named. No new schema, no new trust computation: it joins
/// <see cref="Entities.PlaybookEntry"/> rows already durable since M4 against
/// <see cref="IRecoveryLedger"/>'s existing entity-scoped history
/// (<see cref="IRecoveryLedger.FindEntriesForEntitySinceAsync"/>). Scoped to the two proposal
/// kinds that name a single entity today (<c>AnomalyFlag</c> — I3, <c>DriftFinding</c> — P2);
/// <c>CorrelationHypothesis</c> spans multiple entities and is covered instead by
/// <see cref="ICorrelationAccountabilityService"/> (C4). Extending this same engine to
/// prevention-rule backtesting (P5) is future work for once prevention rules exist to execute —
/// the roadmap's own "same engine, second application."
/// </summary>
public interface IBacktestService
{
    /// <summary>Builds a backtest report for the given owner, optionally narrowed to one pillar.</summary>
    /// <param name="ownerId">Tenant/owner identifier for isolation.</param>
    /// <param name="pillarKind">Optional pillar filter (only <c>Investigate</c>/<c>Prevent</c>
    /// proposals are ever backtestable today).</param>
    /// <param name="limit">Maximum number of dispositioned proposals to backtest, most recently
    /// proposed first (1-200, default 50).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<BacktestReport> GetReportAsync(
        string ownerId,
        PillarKind? pillarKind = null,
        int limit = 50,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One dispositioned proposal's counterfactual result: what a human decided, and whether the
/// Recovery Evidence Ledger recorded any real recovery activity for the same entity from the
/// moment it was proposed onward. <see cref="Corroborated"/> is the honest signal this exists to
/// produce — it says nothing about whether the human's decision was right, only whether reality
/// backed up the finding.
/// </summary>
public sealed record BacktestEntryResult(
    Guid PlaybookEntryId,
    PillarKind PillarKind,
    string ProposalKind,
    string EntityName,
    Guid? NamespaceId,
    DateTimeOffset ProposedAt,
    string Disposition,
    int SubsequentRecoveryAttempts,
    int SubsequentRecoveredCount,
    int SubsequentReturnedCount,
    bool Corroborated);

/// <summary>
/// A snapshot of how often ServiceHub's proactive findings (I3/P2) were followed by real
/// recovery activity for the same entity. <see cref="CorroborationRate"/> is null until at least
/// one proposal has been backtested — an honest "not enough evidence yet" rather than a
/// fabricated 0%, matching <see cref="CorrelationAccountabilityReport"/>'s convention.
/// </summary>
public sealed record BacktestReport(
    DateTimeOffset GeneratedAt,
    int TotalBacktested,
    int CorroboratedCount,
    double? CorroborationRate,
    IReadOnlyList<BacktestEntryResult> Entries);
