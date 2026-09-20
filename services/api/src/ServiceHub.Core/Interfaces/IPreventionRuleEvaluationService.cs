using ServiceHub.Core.Entities;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// P5's "Observe/Evaluate" step (<c>PREVENTION-RULE-DESIGN-2026-08-29.md</c> §1/§4/§8/§9) — reads
/// promoted <c>PreventionRuleProposal</c> entries and the P1/P2 drift signal, both already
/// durable/computed elsewhere, and writes <c>PreventionTrigger</c> evidence. Never calls anything
/// but <see cref="IPlaybookLedger"/>; never mutates a queue, an <c>AutoReplayRule</c>, or the
/// Recovery Evidence Ledger (§12).
/// </summary>
public interface IPreventionRuleEvaluationService
{
    /// <summary>
    /// Evaluates every currently-active <c>PreventionRuleProposal</c> for <paramref name="ns"/>
    /// against this detection cycle's fresh <see cref="DriftFinding"/>s, writing a
    /// <c>PreventionTrigger</c>-kind <see cref="Entities.PlaybookEntry"/> for every match. Called
    /// as a second pass after <c>DriftDetectionWorker</c>'s own P1/P2 cycle — P1/P2 themselves are
    /// unmodified and unaware this exists (§4).
    /// </summary>
    Task EvaluateAsync(Namespace ns, IReadOnlyList<DriftFinding> findings, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the currently active (promoted, non-revoked, non-superseded) rule for each distinct
    /// rule lineage in scope — "the currently active rule" is a derived read, never new stored
    /// state (§8): if more than one version of the same lineage is still <c>Approved</c> (possible
    /// when an edit's new version was approved but the prior version was never explicitly closed
    /// out), the newest wins here. Deliberately a pure read — it never revokes the stale version
    /// itself, so a <c>PlaybookRead</c>-scoped caller (e.g. <c>GET /prevention-rules/active</c>)
    /// can never trigger a governed ledger mutation merely by listing. That reconciliation happens
    /// separately, only from the system-authored evaluation path.
    /// </summary>
    Task<IReadOnlyList<PlaybookEntry>> GetActiveRulesAsync(
        string ownerId, Guid? namespaceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Revokes every promoted rule owned by <paramref name="ownerId"/> whose
    /// <c>PreventionRuleProposal.RuleExpiresAt</c> is at or before <paramref name="asOf"/> and
    /// nobody has reconfirmed (§9) — the query <c>PreventionRuleExpiryWorker</c>'s sweep drives.
    /// Returns the number of rules revoked.
    /// </summary>
    Task<int> SweepExpiredRulesAsync(string ownerId, DateTimeOffset asOf, CancellationToken cancellationToken = default);
}
