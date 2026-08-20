using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// <inheritdoc cref="IRecoveryTrustScoringService"/>
/// </summary>
/// <remarks>
/// Every number here is a deterministic query or ratio over <see cref="IRecoveryLedger"/> data —
/// no ML-based definition appears anywhere (roadmap §8.10). <c>unsafe_outcome_count</c> and
/// <c>duplicate_association</c> are sourced from operator-attested
/// <c>RecoveryEventType.OutcomeFlagged</c> events — <c>Unsafe</c> is checked fleet-wide for the
/// owner, <c>DuplicateBusinessEffect</c> per-signature (§8.10).
/// </remarks>
public sealed class RecoveryTrustScoringService : IRecoveryTrustScoringService
{
    /// <summary>L3→L4 gate's minimum sample size (roadmap §8.7, §8.10).</summary>
    public const int L4MinimumSample = 10;

    /// <summary>L3→L4 gate's minimum verified success rate (roadmap §8.7, §8.10).</summary>
    public const double L4MinimumRate = 0.95;

    /// <summary>L4→L5 gate's minimum sample size (roadmap §8.7, §8.10).</summary>
    public const int L5MinimumSample = 30;

    /// <summary>L4→L5 gate's minimum verified success rate (roadmap §8.7, §8.10).</summary>
    public const double L5MinimumRate = 0.99;

    private readonly IRecoveryLedger _recoveryLedger;

    /// <summary>Initialises a new instance of <see cref="RecoveryTrustScoringService"/>.</summary>
    public RecoveryTrustScoringService(IRecoveryLedger recoveryLedger)
    {
        _recoveryLedger = recoveryLedger ?? throw new ArgumentNullException(nameof(recoveryLedger));
    }

    /// <inheritdoc />
    public async Task<Result<SignatureTrustEvidence>> EvaluateAsync(
        string ownerId, string signatureHash, RecoveryOperationKind actionKind,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(signatureHash))
        {
            return Result<SignatureTrustEvidence>.Failure(Error.Validation(
                "TrustScoring.SignatureHashRequired",
                "A non-null, non-empty SignatureHash is required — a signature with no stable " +
                "hash has no per-signature trust identity to evaluate (roadmap §4)."));
        }

        var counts = await _recoveryLedger.GetDispositionCountsAsync(
            ownerId, signatureHash, actionKind, cancellationToken);
        var unsafeOutcomePresent = await _recoveryLedger.HasUnsafeOutcomeFlagAsync(ownerId, cancellationToken);
        var duplicateAssociationPresent = await _recoveryLedger.HasDuplicateAssociationAsync(
            ownerId, signatureHash, cancellationToken);

        var recovered = counts.GetValueOrDefault(RecoveryDisposition.Recovered);
        var returned = counts.GetValueOrDefault(RecoveryDisposition.Returned);
        var failed = counts.GetValueOrDefault(RecoveryDisposition.Failed);
        var unverified = counts.GetValueOrDefault(RecoveryDisposition.Unverified);
        var declined = counts.GetValueOrDefault(RecoveryDisposition.Declined);

        // §8.10: denominator is Recovered + Returned + Failed only. Unverified is excluded from
        // both numerator and denominator (a provider limitation is not evidence against the
        // signature, §14); Declined never entered execution, so it never appears here either;
        // Discarded (purge) cannot appear because GetDispositionCountsAsync already scopes to
        // actionKind via the parent operation's Kind.
        var sampleSize = recovered + returned + failed;
        double? verifiedSuccessRate = sampleSize > 0 ? (double)recovered / sampleSize : null;

        var meetsL4 = sampleSize >= L4MinimumSample && verifiedSuccessRate >= L4MinimumRate;
        var meetsL5 = sampleSize >= L5MinimumSample && verifiedSuccessRate >= L5MinimumRate;

        var reasons = BuildReasons(
            sampleSize, verifiedSuccessRate, meetsL4, meetsL5, unsafeOutcomePresent, duplicateAssociationPresent);

        var evidence = new SignatureTrustEvidence(
            OwnerId: ownerId,
            SignatureHash: signatureHash,
            ActionKind: actionKind,
            RecoveredCount: recovered,
            ReturnedCount: returned,
            FailedCount: failed,
            UnverifiedCount: unverified,
            DeclinedCount: declined,
            SampleSize: sampleSize,
            VerifiedSuccessRate: verifiedSuccessRate,
            MeetsL4SampleAndRate: meetsL4,
            MeetsL5SampleAndRate: meetsL5,
            UnsafeOutcomePresent: unsafeOutcomePresent,
            DuplicateAssociationPresent: duplicateAssociationPresent,
            Reasons: reasons);

        return Result<SignatureTrustEvidence>.Success(evidence);
    }

    private static List<string> BuildReasons(
        int sampleSize, double? verifiedSuccessRate, bool meetsL4, bool meetsL5,
        bool unsafeOutcomePresent, bool duplicateAssociationPresent)
    {
        var reasons = new List<string>();

        if (sampleSize == 0)
        {
            reasons.Add("No verified execution evidence exists yet for this signature — starts at L0 (roadmap §8.1).");
            reasons.Add("L4/L5 are unreachable with insufficient evidence regardless of any other signal (fail-safe: insufficient evidence is never reported as trust).");
        }
        else
        {
            reasons.Add($"{sampleSize} verified execution(s) counted ({verifiedSuccessRate:P0} verified success rate).");

            if (!meetsL4)
            {
                reasons.Add(sampleSize < L4MinimumSample
                    ? $"L3→L4 requires a sample of at least {L4MinimumSample}; only {sampleSize} verified execution(s) exist."
                    : $"L3→L4 requires a verified success rate of at least {L4MinimumRate:P0}; current rate is {verifiedSuccessRate:P0}.");
            }
            else if (!meetsL5)
            {
                reasons.Add(sampleSize < L5MinimumSample
                    ? $"L4→L5 requires a sample of at least {L5MinimumSample}; only {sampleSize} verified execution(s) exist."
                    : $"L4→L5 requires a verified success rate of at least {L5MinimumRate:P0}; current rate is {verifiedSuccessRate:P0}.");
            }
        }

        if (unsafeOutcomePresent)
        {
            reasons.Add("An operator has flagged an unsafe outcome somewhere in this owner's fleet — L4/L5 are withheld fleet-wide until resolved (§8.10).");
        }

        if (duplicateAssociationPresent)
        {
            reasons.Add("An operator has flagged a duplicate business effect for this signature — a permanent L4/L5 disqualifier, not restorable without a separate explicit human act (§8.10).");
        }

        return reasons;
    }
}
