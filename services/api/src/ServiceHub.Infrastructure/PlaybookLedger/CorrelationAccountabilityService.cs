using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.PlaybookLedger;

/// <summary>
/// <inheritdoc cref="ICorrelationAccountabilityService"/>
/// </summary>
/// <remarks>
/// Reads <see cref="IPlaybookLedger.QueryEntriesAsync"/> for the caller's <c>Correlate</c>-pillar
/// entries and groups by <see cref="PlaybookEntry.State"/> — mirroring
/// <c>AutonomyDashboardService</c>'s "count, filter, or grouping over rows already written"
/// discipline. No schema change, no new trust computation.
/// </remarks>
public sealed class CorrelationAccountabilityService : ICorrelationAccountabilityService
{
    private const string CorrelationHypothesisProposalKind = "CorrelationHypothesis";

    private readonly IPlaybookLedger _playbookLedger;

    /// <summary>Initializes a new instance of <see cref="CorrelationAccountabilityService"/>.</summary>
    public CorrelationAccountabilityService(IPlaybookLedger playbookLedger)
    {
        _playbookLedger = playbookLedger ?? throw new ArgumentNullException(nameof(playbookLedger));
    }

    /// <inheritdoc/>
    public async Task<CorrelationAccountabilityReport> GetReportAsync(
        string ownerId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ownerId))
        {
            throw new ArgumentException("Owner identifier is required.", nameof(ownerId));
        }

        var result = await _playbookLedger.QueryEntriesAsync(
            ownerId, PillarKind.Correlate, cancellationToken: cancellationToken);

        // A query failure reports zero rather than throwing — this is a dashboard, not a
        // correctness-critical read; an empty report is honest degraded behavior, not a lie.
        var hypotheses = result.IsSuccess
            ? result.Value.Where(e => e.ProposalKind == CorrelationHypothesisProposalKind).ToList()
            : new List<PlaybookEntry>();

        int CountIn(PlaybookEntryState state) => hypotheses.Count(e => e.State == state);

        var approvedCount = CountIn(PlaybookEntryState.Approved);
        var rejectedCount = CountIn(PlaybookEntryState.Rejected);
        var dispositionedCount = approvedCount + rejectedCount;

        return new CorrelationAccountabilityReport(
            GeneratedAt: DateTimeOffset.UtcNow,
            TotalHypotheses: hypotheses.Count,
            ProposedCount: CountIn(PlaybookEntryState.Proposed),
            UnderReviewCount: CountIn(PlaybookEntryState.UnderReview) + CountIn(PlaybookEntryState.Edited),
            ApprovedCount: approvedCount,
            RejectedCount: rejectedCount,
            ExpiredCount: CountIn(PlaybookEntryState.Expired),
            SupersededCount: CountIn(PlaybookEntryState.Superseded),
            ApprovalRate: dispositionedCount > 0 ? (double)approvedCount / dispositionedCount : null);
    }
}
