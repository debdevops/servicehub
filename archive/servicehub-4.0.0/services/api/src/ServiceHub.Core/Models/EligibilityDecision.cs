using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// <see cref="Interfaces.IRecoveryEligibilityGate.EvaluateAsync"/>'s result — the verdict plus
/// enough detail for the caller to write a truthful <c>Declined</c> ledger entry (roadmap §9.3)
/// when appropriate. <see cref="ReasonCode"/> is <see langword="null"/> unless either a predicate
/// produced a non-<c>Allow</c> verdict, or predicate 3's recurrence cap was reached by a
/// <c>User</c>/<c>ApiKey</c> actor (roadmap §9.4.1) — carried as observability context on an
/// <see cref="EligibilityVerdict.Allow"/> verdict, never a denial, per §29.11 Option B.
/// </summary>
/// <param name="Verdict">The gate's decision.</param>
/// <param name="ReasonCode">Which predicate produced a non-<c>Allow</c> verdict, e.g.
/// <c>"PURGE_AUTOMATION_PROHIBITED"</c>, <c>"RECURRENCE_CAP_EXCEEDED"</c> — or, when
/// <see cref="Verdict"/> is <see cref="EligibilityVerdict.Allow"/>, the recurrence-cap reason code
/// observed for a human actor (§9.4.1). Distinguishes predicate 4 (rate limit — routine
/// throttling, never <c>Declined</c>-recorded per §9.3) from predicates 1/2/3 (real safety
/// escalations, always <c>Declined</c>-recorded when they fire and terminal).</param>
/// <param name="DetailJson">Optional structured detail for the ledger's <c>DetailJson</c> column.</param>
/// <param name="MatchedCount">Predicate 3 only: how many prior lineage-matched entries were found.</param>
public sealed record EligibilityDecision(
    EligibilityVerdict Verdict,
    string? ReasonCode,
    string? DetailJson = null,
    int MatchedCount = 0)
{
    /// <summary>The single shared <see cref="EligibilityVerdict.Allow"/> instance — no predicate fired.</summary>
    public static readonly EligibilityDecision Allow = new(EligibilityVerdict.Allow, ReasonCode: null);
}
