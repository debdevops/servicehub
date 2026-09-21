using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// The single deterministic safety-decision point every recovery attempt passes through before a
/// provider call — human, automation, or any future AI-originated recommendation, same gate
/// instance, same predicate order (roadmap §9). Never itself an AI agent: predicates read only
/// deterministic ledger/provider-capability data and produce a reproducible verdict. The gate
/// makes no ledger writes — <see cref="Models.EligibilityDecision"/> carries enough detail for the
/// caller to record a <c>Declined</c> entry (<see cref="IRecoveryLedger.RecordDeclinedAsync"/>)
/// itself, using the same <c>RecoveryLedgerEntrySnapshot</c>/actor context it already has on hand.
/// </summary>
public interface IRecoveryEligibilityGate
{
    /// <summary>
    /// Evaluates every predicate in order and returns the first non-<c>Allow</c> verdict, or
    /// <see cref="EligibilityDecision.Allow"/> if none fired. Fails closed to
    /// <see cref="Enums.EligibilityVerdict.Escalate"/> — never <c>Allow</c> — if a predicate's
    /// own query (e.g. the recurrence-lineage lookup) throws.
    /// </summary>
    Task<EligibilityDecision> EvaluateAsync(
        RecoveryEligibilityRequest request,
        CancellationToken cancellationToken = default);
}
