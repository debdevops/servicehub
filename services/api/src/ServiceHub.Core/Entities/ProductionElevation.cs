namespace ServiceHub.Core.Entities;

/// <summary>
/// One time-boxed, two-person-approved window granting recovery access to a single Prod
/// namespace (ADR-0010 §Decision phase 2). Mutable by design, unlike the rest of the Recovery
/// Evidence Ledger — like <see cref="AutonomyGrant"/>, this row is never the sole record of a
/// transition, only its queryable current state; every request/approve/revoke additionally
/// writes an immutable, hash-chained <see cref="RecoveryEvent"/>
/// (<see cref="Interfaces.IRecoveryLedger.RequestProductionElevationAsync"/>,
/// <see cref="Interfaces.IRecoveryLedger.ApproveProductionElevationAsync"/>,
/// <see cref="Interfaces.IRecoveryLedger.RevokeProductionElevationAsync"/>).
/// <para>
/// An elevation is a subject, a reason, and an expiry — never a flag. Expiry is absolute
/// wall-clock and cannot be extended: a longer window means a new elevation with a new approval.
/// </para>
/// </summary>
public sealed class ProductionElevation
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner ID for multi-tenant isolation. Same format as <see cref="DlqMessage.OwnerId"/>.</summary>
    public required string OwnerId { get; init; }

    /// <summary>The Prod namespace this elevation covers — soft reference, no FK (same convention
    /// as every other ledger-adjacent NamespaceId).</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Namespace display name, snapshotted at request time — survives namespace deletion.</summary>
    public string? NamespaceNameSnapshot { get; init; }

    /// <summary>The stated reason for requesting production access. Required — an elevation with
    /// no reason is not an elevation.</summary>
    public required string Reason { get; init; }

    /// <summary>The resolved actor identity that requested this elevation — never caller-supplied.
    /// Must hold <see cref="Enums.GovernanceRole.Operator"/> on <see cref="NamespaceId"/> at
    /// request time.</summary>
    public required string RequestedByIdentity { get; init; }

    /// <summary>When this elevation was requested.</summary>
    public required DateTimeOffset RequestedAt { get; init; }

    /// <summary>The duration requested at request time — applied to <see cref="ExpiresAt"/> when
    /// (and only when) the elevation is approved. Stored here, not just in the request event's
    /// <c>DetailJson</c>, so approval needs no second read of the hash chain to learn it.</summary>
    public required TimeSpan RequestedDuration { get; init; }

    /// <summary>The resolved actor identity that approved this elevation — never caller-supplied,
    /// and never equal to <see cref="RequestedByIdentity"/> (dual control admits no self-approval,
    /// including for <see cref="Enums.GovernanceRole.Admin"/>). Null while pending approval.</summary>
    public string? ApprovedByIdentity { get; set; }

    /// <summary>When this elevation was approved. Null while pending approval.</summary>
    public DateTimeOffset? ApprovedAt { get; set; }

    /// <summary>Absolute wall-clock expiry, set once at approval time and never extended. Null
    /// while pending approval — an unapproved elevation grants nothing regardless of this field.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    /// <summary>When this elevation was revoked early, or null if it has not been. Set once, never
    /// cleared.</summary>
    public DateTimeOffset? RevokedAt { get; set; }

    /// <summary>The resolved actor identity that revoked this elevation — never caller-supplied.
    /// Null while <see cref="RevokedAt"/> is null.</summary>
    public string? RevokedByIdentity { get; set; }

    /// <summary>
    /// Whether <see cref="Enums.RecoveryEventType.ProductionElevationExpired"/> has already been
    /// recorded for this row — lets <c>ProductionElevationExpiryWorker</c> idempotently sweep
    /// naturally-lapsed elevations exactly once, mirroring
    /// <see cref="Interfaces.IRecoveryLedger.HasAgeingFlagAsync"/>'s role for
    /// <see cref="Enums.RecoveryEntryState.Expired"/>.
    /// </summary>
    public bool ExpiredEventRecorded { get; set; }

    /// <summary>
    /// Whether this elevation currently grants live production recovery access: approved, not
    /// revoked, and not yet past <see cref="ExpiresAt"/>. This is the single predicate the
    /// Recovery Eligibility Gate's predicate 2 and every front-door Prod guard evaluate.
    /// </summary>
    public bool IsLiveAt(DateTimeOffset asOf) =>
        ApprovedAt is not null
        && RevokedAt is null
        && ExpiresAt is { } expiresAt
        && expiresAt > asOf;
}
