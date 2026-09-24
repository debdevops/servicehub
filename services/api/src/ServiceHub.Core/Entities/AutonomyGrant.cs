using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// The current authorization projection for one (owner, signature, action) triple — how much
/// unattended autonomy a failure signature has earned (roadmap §8, §9.4.3). Mutable by design,
/// unlike the rest of the Recovery Evidence Ledger: every promotion or demotion updates this row
/// and, in the same save, writes an immutable, hash-chained <see cref="RecoveryEvent"/>
/// (<see cref="Interfaces.IRecoveryLedger.RecordAutonomyGrantTransitionAsync"/>) — this row is
/// never the sole record of a transition, only its queryable current state.
/// </summary>
public sealed class AutonomyGrant
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner ID for multi-user isolation. Same format as <see cref="DlqMessage.OwnerId"/>.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The failure signature this grant applies to.</summary>
    public required string SignatureHash { get; init; }

    /// <summary>The recovery action this grant applies to.</summary>
    public required RecoveryOperationKind ActionKind { get; init; }

    /// <summary>The currently earned autonomy level for this (owner, signature, action) triple.</summary>
    public required AutonomyLevel CurrentLevel { get; set; }

    /// <summary>Informational only — last-evaluated timestamp. Carries no concurrency-safety
    /// role; <see cref="ConcurrencyStamp"/> is the actual optimistic-concurrency token
    /// (roadmap §9.4.4 Correction).</summary>
    public required DateTimeOffset UpdatedAtUtc { get; set; }

    /// <summary>
    /// The optimistic-concurrency token. Stamped centrally by <c>DlqDbContext</c>'s
    /// <c>SaveChanges</c>/<c>SaveChangesAsync</c> overrides on every insert or update — never a
    /// caller responsibility (roadmap §9.4.4 Correction: a future change to
    /// <see cref="CurrentLevel"/> that forgot to also bump this field would otherwise silently
    /// defeat the concurrency check).
    /// </summary>
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
}
