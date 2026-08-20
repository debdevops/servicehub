using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Evidence-Derived Trust Scoring (roadmap §8.10, Phase C) — computes a
/// <see cref="SignatureTrustEvidence"/> report purely from the Recovery Evidence Ledger's
/// persisted outcomes. Never writes anything, never consults AI/model confidence, and never
/// implies an autonomy grant exists: trust here is a report of past verified outcomes, not a
/// decision. See <see cref="IRecoveryLedger"/> for the ledger this reads.
/// </summary>
public interface IRecoveryTrustScoringService
{
    /// <summary>
    /// Evaluates one signature's evidence for one action kind. Fails with a validation error if
    /// <paramref name="signatureHash"/> is null/empty — a signature with no stable hash has no
    /// per-signature trust identity to evaluate (roadmap §4): <c>BodyHash</c> lineage evidence is
    /// never substituted for a missing <c>SignatureHash</c>.
    /// </summary>
    Task<Result<SignatureTrustEvidence>> EvaluateAsync(
        string ownerId,
        string signatureHash,
        RecoveryOperationKind actionKind,
        CancellationToken cancellationToken = default);
}
