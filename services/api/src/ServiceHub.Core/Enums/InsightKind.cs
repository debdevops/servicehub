namespace ServiceHub.Core.Enums;

/// <summary>
/// Discriminates which detection pillar produced an <c>InsightDetected</c> Platform Event
/// (roadmap §5, I5 — "Push"). One event type/payload/notifier method serves all four kinds
/// rather than four near-identical ones, the same "generic once" reasoning the roadmap's own
/// Playbook Ledger design applies to <c>PillarKind</c>/<c>ProposalKind</c> — these four facts
/// (anomaly, drift, correlation, narration) share an identical shape (a namespace-or-owner-scoped
/// finding with an ID, severity, and description), so a discriminated event is a real capability,
/// not a shortcut past the strongly-typed-per-fact pattern used elsewhere.
/// </summary>
public enum InsightKind
{
    /// <summary>An <see cref="Entities.Anomaly"/> (roadmap §5.B, I3).</summary>
    Anomaly = 0,

    /// <summary>A <see cref="Entities.DriftFinding"/> (roadmap §5.C, P1/P2).</summary>
    Drift = 1,

    /// <summary>A <see cref="Entities.CorrelationFinding"/> (roadmap §5.D, C1).</summary>
    Correlation = 2,

    /// <summary>A <see cref="Entities.Narration"/> (roadmap §5.B, I4).</summary>
    Narration = 3,
}
