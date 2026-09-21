namespace ServiceHub.Core.Enums;

/// <summary>
/// How a <see cref="VerificationResult.Returned"/> recurrence was attributed to a specific
/// <see cref="Entities.RecoveryLedgerEntry"/>.
/// </summary>
public enum VerificationConfidence
{
    /// <summary>Matched via the <c>x-servicehub-recovery-id</c> marker — unambiguous.</summary>
    Exact = 0,

    /// <summary>Matched via body-hash fallback only — the marker could not be applied, and the
    /// hash is stable but not unique, so the match may be shared with other open entries.</summary>
    Heuristic = 1
}
