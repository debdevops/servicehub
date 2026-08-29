namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Correlation accountability (roadmap §5.D C4, §11 item 17) — "once the Playbook Ledger exists,
/// every C1–C3 hypothesis is logged with human disposition, making correlation quality measurable
/// instead of a black box." Pure read-side aggregation over <see cref="IPlaybookLedger"/>'s
/// existing Correlate-pillar entries — no new schema, no new trust computation. C3 (external-signal
/// correlation) contributes no hypotheses yet since it remains gated behind M5; this report reads
/// whatever the ledger holds today, which is C1/C2 only until then.
/// </summary>
public interface ICorrelationAccountabilityService
{
    /// <summary>Builds a correlation accountability report for the given owner.</summary>
    /// <param name="ownerId">Tenant/owner identifier for isolation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CorrelationAccountabilityReport> GetReportAsync(
        string ownerId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// A snapshot of how many correlation hypotheses ServiceHub has proposed into the Playbook Ledger
/// and what humans decided about them. <see cref="ApprovalRate"/> is null until at least one
/// hypothesis has reached a terminal human disposition (Approved/Rejected) — an honest "not enough
/// evidence yet" rather than a fabricated 0%.
/// </summary>
public sealed record CorrelationAccountabilityReport(
    DateTimeOffset GeneratedAt,
    int TotalHypotheses,
    int ProposedCount,
    int UnderReviewCount,
    int ApprovedCount,
    int RejectedCount,
    int ExpiredCount,
    int SupersededCount,
    double? ApprovalRate);
