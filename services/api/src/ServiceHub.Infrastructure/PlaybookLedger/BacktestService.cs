using System.Text.Json;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.PlaybookLedger;

/// <summary>
/// <inheritdoc cref="IBacktestService"/>
/// </summary>
/// <remarks>
/// Reads <see cref="IPlaybookLedger.QueryEntriesAsync"/> for the caller's dispositioned,
/// single-entity proposals, extracts each one's <c>EntityName</c> from its opaque
/// <c>ProposalJson</c> (the one place this service is proposal-kind-aware rather than treating
/// the payload as opaque), and joins that against
/// <see cref="IRecoveryLedger.FindEntriesForEntitySinceAsync"/> — mirroring
/// <c>CorrelationAccountabilityService</c>'s "count, filter, or grouping over rows already
/// written" discipline. No schema change, no new trust computation. Roadmap item 14's "same
/// engine, second application" on the Recover side: <c>ReplayPlan</c> proposals (from
/// <c>AutoReplayExecutor</c>, when predicate 5 escalates for lack of earned autonomy) carry
/// <c>EntityName</c> in the same shape as <c>AnomalyFlag</c>/<c>DriftFinding</c>, so they join
/// identically — no separate signature-hash code path needed.
/// </remarks>
public sealed class BacktestService : IBacktestService
{
    private static readonly IReadOnlyCollection<string> BacktestableProposalKinds =
        new[] { "AnomalyFlag", "DriftFinding", "ReplayPlan", "PreventionTrigger" };

    // PreventionTrigger is deliberately exempt from the Approved/Rejected requirement below: per
    // PREVENTION-RULE-DESIGN-2026-08-29.md §12, "a PreventionTrigger is never a decision request"
    // — it is pure evidence a human is never asked to approve or reject, so in normal operation it
    // never leaves Proposed until it expires. Gating candidacy on Approved/Rejected (as every
    // other ProposalKind here correctly requires — "dispositioned proposals") would make a
    // PreventionTrigger permanently unreachable, silently turning P5's backtest into dead code. It
    // becomes a candidate in any state once it exists at all — the rare edge case of a human
    // dispositioning one anyway (the generic Playbook disposition endpoint doesn't restrict by
    // ProposalKind) is still valid evidence, not a case to special-case out.
    private static readonly IReadOnlySet<PlaybookEntryState> BacktestableDispositionedStates =
        new HashSet<PlaybookEntryState> { PlaybookEntryState.Approved, PlaybookEntryState.Rejected };

    private const string PreventionTriggerProposalKind = "PreventionTrigger";

    private static bool IsBacktestCandidate(PlaybookEntry entry) =>
        entry.NamespaceId is not null
        && BacktestableProposalKinds.Contains(entry.ProposalKind)
        && (entry.ProposalKind == PreventionTriggerProposalKind
            || BacktestableDispositionedStates.Contains(entry.State));

    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;
    private const int RecoveryLookbackLimit = 20;

    private readonly IPlaybookLedger _playbookLedger;
    private readonly IRecoveryLedger _recoveryLedger;

    /// <summary>Initializes a new instance of <see cref="BacktestService"/>.</summary>
    public BacktestService(IPlaybookLedger playbookLedger, IRecoveryLedger recoveryLedger)
    {
        _playbookLedger = playbookLedger ?? throw new ArgumentNullException(nameof(playbookLedger));
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
    }

    /// <inheritdoc/>
    public async Task<BacktestReport> GetReportAsync(
        string ownerId, PillarKind? pillarKind = null, int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner identifier is required.", nameof(ownerId));
        }

        var result = await _playbookLedger.QueryEntriesAsync(
            ownerId, pillarKind, cancellationToken: cancellationToken);

        // A query failure reports zero rather than throwing — this is a dashboard, not a
        // correctness-critical read, same reasoning as CorrelationAccountabilityService.
        if (result.IsFailure)
        {
            return new BacktestReport(DateTimeOffset.UtcNow, 0, 0, null, Array.Empty<BacktestEntryResult>());
        }

        var candidates = result.Value
            .Where(IsBacktestCandidate)
            .OrderByDescending(e => e.ProposedAt)
            .Take(Math.Clamp(limit, 1, MaxLimit))
            .ToList();

        var backtested = new List<BacktestEntryResult>();
        foreach (var entry in candidates)
        {
            var entityName = ExtractEntityName(entry.ProposalJson);
            if (entityName is null)
            {
                continue;
            }

            var subsequent = await _recoveryLedger.FindEntriesForEntitySinceAsync(
                ownerId, entry.NamespaceId, entityName, entry.ProposedAt, RecoveryLookbackLimit, cancellationToken);

            backtested.Add(new BacktestEntryResult(
                PlaybookEntryId: entry.Id,
                PillarKind: entry.PillarKind,
                ProposalKind: entry.ProposalKind,
                EntityName: entityName,
                NamespaceId: entry.NamespaceId,
                ProposedAt: entry.ProposedAt,
                Disposition: entry.Disposition?.ToString() ?? entry.State.ToString(),
                SubsequentRecoveryAttempts: subsequent.Count,
                SubsequentRecoveredCount: subsequent.Count(e => e.Disposition == RecoveryDisposition.Recovered),
                SubsequentReturnedCount: subsequent.Count(e => e.Disposition == RecoveryDisposition.Returned),
                Corroborated: subsequent.Count > 0));
        }

        var corroboratedCount = backtested.Count(e => e.Corroborated);

        return new BacktestReport(
            GeneratedAt: DateTimeOffset.UtcNow,
            TotalBacktested: backtested.Count,
            CorroboratedCount: corroboratedCount,
            CorroborationRate: backtested.Count > 0 ? (double)corroboratedCount / backtested.Count : null,
            Entries: backtested);
    }

    /// <summary>
    /// Extracts <c>EntityName</c> from a proposal's JSON payload — the one join key
    /// <c>AnomalyFlag</c> and <c>DriftFinding</c> proposals both carry (see
    /// <c>AnomalyDetectionWorker</c>/<c>DriftDetectionWorker</c>'s <c>ProposePlaybookEntryAsync</c>).
    /// Malformed or missing JSON yields <see langword="null"/> rather than throwing — this is a
    /// best-effort read over an opaque payload the ledger itself never validates.
    /// </summary>
    private static string? ExtractEntityName(string proposalJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(proposalJson);
            return doc.RootElement.TryGetProperty("EntityName", out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
