using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// Evidence-Derived Trust Scoring's read-only result for one <c>(OwnerId, SignatureHash,
/// ActionKind)</c> triple (roadmap §8.10) — a report of what the Recovery Evidence Ledger
/// actually shows, never an autonomy decision. Producing this record never writes anything and
/// never implies any <c>AutonomyGrant</c> exists — that write path is Phase D's, not this one's.
/// </summary>
/// <param name="OwnerId">Tenant scope this evidence was computed for.</param>
/// <param name="SignatureHash">The failure signature this evidence concerns.</param>
/// <param name="ActionKind">The recovery action this evidence concerns — <c>Purge</c> is never
/// autonomous (roadmap §9.1) and structurally never accumulates a non-zero sample here.</param>
/// <param name="RecoveredCount">Terminal <see cref="RecoveryDisposition.Recovered"/> entries.</param>
/// <param name="ReturnedCount">Terminal <see cref="RecoveryDisposition.Returned"/> entries.</param>
/// <param name="FailedCount">Terminal <see cref="RecoveryDisposition.Failed"/> entries.</param>
/// <param name="UnverifiedCount">Terminal <see cref="RecoveryDisposition.Unverified"/> entries —
/// excluded from <see cref="SampleSize"/> and <see cref="VerifiedSuccessRate"/> by definition
/// (§8.10, §14): a provider's inability to prove absence is not evidence against the signature.</param>
/// <param name="DeclinedCount">Terminal <see cref="RecoveryDisposition.Declined"/> entries —
/// the safety gate blocked the attempt before any provider was contacted, so no execution
/// outcome exists to score; reported for visibility only, never counted.</param>
/// <param name="SampleSize"><c>RecoveredCount + ReturnedCount + FailedCount</c> — the exact
/// denominator population §8.10 defines for <see cref="VerifiedSuccessRate"/>.</param>
/// <param name="VerifiedSuccessRate"><c>RecoveredCount / SampleSize</c>. <see langword="null"/>
/// when <see cref="SampleSize"/> is zero — insufficient evidence is never reported as a rate of
/// zero, which would misrepresent "no data" as "confirmed failure."</param>
/// <param name="MeetsL4SampleAndRate">Whether the numeric part of the L3→L4 gate is satisfied:
/// <c>SampleSize ≥ 10</c> and <c>VerifiedSuccessRate ≥ 0.95</c> (§8.7, §8.10). Does not by
/// itself mean L4 is reachable — see <see cref="UnsafeOutcomePresent"/>/<see cref="DuplicateAssociationPresent"/>.</param>
/// <param name="MeetsL5SampleAndRate">Whether the numeric part of the L4→L5 gate is satisfied:
/// <c>SampleSize ≥ 30</c> and <c>VerifiedSuccessRate ≥ 0.99</c> (§8.7, §8.10).</param>
/// <param name="UnsafeOutcomePresent">
/// §8.10's <c>unsafe_outcome_count &gt; 0</c> disqualifier, sourced from operator-attested
/// <c>RecoveryEventType.OutcomeFlagged</c> events with <c>FlagKind = Unsafe</c>. Fleet-level, not
/// scoped to <see cref="SignatureHash"/> — a confirmed unsafe outcome anywhere in
/// <see cref="OwnerId"/>'s fleet disqualifies every signature under that owner, not only the
/// flagged one (§8.10).
/// </param>
/// <param name="DuplicateAssociationPresent">§8.10's <c>duplicate_association</c> permanent
/// disqualifier, sourced from <c>OutcomeFlagged</c> events with
/// <c>FlagKind = DuplicateBusinessEffect</c>. Per-signature, unlike
/// <see cref="UnsafeOutcomePresent"/> — scoped strictly to <see cref="SignatureHash"/>, never
/// diluted by volume and never bleeding into an unrelated signature under the same owner.</param>
/// <param name="Reasons">Human-readable statements of what evidence exists and what is
/// missing — the explainability surface for "why is this signature at its current level" and
/// "why has it not earned a higher one" (roadmap §22, Phase G).</param>
public sealed record SignatureTrustEvidence(
    string OwnerId,
    string SignatureHash,
    RecoveryOperationKind ActionKind,
    int RecoveredCount,
    int ReturnedCount,
    int FailedCount,
    int UnverifiedCount,
    int DeclinedCount,
    int SampleSize,
    double? VerifiedSuccessRate,
    bool MeetsL4SampleAndRate,
    bool MeetsL5SampleAndRate,
    bool UnsafeOutcomePresent,
    bool DuplicateAssociationPresent,
    IReadOnlyList<string> Reasons);
