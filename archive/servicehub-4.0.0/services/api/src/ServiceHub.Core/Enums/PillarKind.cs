namespace ServiceHub.Core.Enums;

/// <summary>
/// The four autonomy pillars (roadmap §3/§14): structural and fixed — a real enum, unlike the
/// deliberately-growing <c>ProposalKind</c> string the future Playbook Ledger (M4) uses per pillar.
/// First used here by <see cref="ServiceHub.Core.Entities.GovernanceGrant"/> (M3) to scope a grant
/// to one pillar; M4 reuses this same enum rather than defining its own.
/// </summary>
public enum PillarKind
{
    /// <summary>Detect-and-recover autonomy ladder (replay/purge).</summary>
    Recover = 0,

    /// <summary>Observe, classify, trend, anomalize, narrate.</summary>
    Investigate = 1,

    /// <summary>Correlate findings across signatures, providers, and external signals.</summary>
    Correlate = 2,

    /// <summary>Baseline drift detection and predictive/producer-facing prevention.</summary>
    Prevent = 3,
}
