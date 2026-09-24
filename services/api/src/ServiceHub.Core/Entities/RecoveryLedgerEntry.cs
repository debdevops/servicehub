using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// One row per (<see cref="RecoveryOperation"/>, message) — the durable evidence record for a
/// single recovery target. The identity/context fields below are immutable once inserted,
/// enforced by the append-only guard in <c>DlqDbContext.SaveChangesAsync</c>; a small mutable
/// projection (<see cref="State"/>, <see cref="Disposition"/>, <see cref="VerificationResult"/>,
/// <see cref="VerificationConfidence"/>, <see cref="ObservationWindowEndsAt"/>,
/// <see cref="LastEventSeq"/>, <see cref="ClosedAt"/>) is writable, but only by
/// <see cref="Interfaces.IRecoveryLedger"/>, and only alongside the <see cref="RecoveryEvent"/>
/// that justifies the change. The evidence is the event chain; this projection is a derived,
/// queryable index over it — the same split <see cref="SignatureLifecycleState"/> uses.
/// </summary>
public sealed class RecoveryLedgerEntry
{
    // ── Immutable identity/context (set once, at BeginEntryAsync) ──────────────

    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>The <see cref="RecoveryOperation"/> this entry belongs to.</summary>
    public required Guid OperationId { get; init; }

    /// <summary>Owner ID for multi-user isolation. Same format as <see cref="DlqMessage.OwnerId"/>.</summary>
    public required string OwnerId { get; init; }

    /// <summary>
    /// Soft reference to the source <see cref="DlqMessage"/> — deliberately **no foreign key**,
    /// so cascade delete of a <see cref="DlqMessage"/> can never reach the ledger.
    /// </summary>
    public long? DlqMessageId { get; init; }

    /// <summary>The namespace this entry's message belonged to.</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>Namespace display name, snapshotted at write time.</summary>
    public string? NamespaceNameSnapshot { get; init; }

    /// <summary>Cloud provider snapshot.</summary>
    public CloudProviderType? ProviderSnapshot { get; init; }

    /// <summary>Deployment environment snapshot.</summary>
    public EnvironmentType? EnvironmentSnapshot { get; init; }

    /// <summary>The queue or subscription name the message was found in.</summary>
    public string? EntityNameSnapshot { get; init; }

    /// <summary>The entity type (queue or subscription), snapshotted.</summary>
    public string? EntityTypeSnapshot { get; init; }

    /// <summary>Topic name, when the entity is a subscription.</summary>
    public string? TopicNameSnapshot { get; init; }

    /// <summary>
    /// The provider-assigned message ID before replay — record only. Replay mints a new
    /// identity on every provider, so this is **not usable as a post-replay recurrence key**.
    /// </summary>
    public string? SourceMessageIdSnapshot { get; init; }

    /// <summary>The sequence number before replay — record only, same caveat as
    /// <see cref="SourceMessageIdSnapshot"/>.</summary>
    public long? SourceSequenceNumberSnapshot { get; init; }

    /// <summary>SHA-256 hash of the message body — the only identity that survives replay on
    /// every provider. Stable, but not unique.</summary>
    public required string BodyHash { get; init; }

    /// <summary>Heuristic failure category, snapshotted for linkage into existing intelligence.</summary>
    public FailureCategory? FailureCategorySnapshot { get; init; }

    /// <summary>Dead-letter reason, snapshotted.</summary>
    public string? DeadLetterReasonSnapshot { get; init; }

    /// <summary>Failure signature hash, snapshotted for linkage into signature lifecycle.</summary>
    public string? SignatureHashSnapshot { get; init; }

    /// <summary>The entity the message was sent to (replay) or the entity it was purged from.</summary>
    public required string TargetEntity { get; init; }

    /// <summary>When this entry was begun.</summary>
    public required DateTimeOffset BegunAt { get; init; }

    // ── Mutable projection (written only by IRecoveryLedger, alongside an event) ──
    //
    // RecoveryMarker/MarkerApplied are grouped here rather than in the immutable block above:
    // the roadmap's domain-model table lists them as immutable, but whether the provider actually
    // accepted the marker attribute (MarkerApplied) genuinely isn't known until the provider call
    // completes — the same moment State/Disposition become known. Marker stamping itself is a
    // later-phase behavioural change to replay; these fields are wired here now so no migration
    // is needed when that phase lands.

    /// <summary>
    /// The <c>x-servicehub-recovery-id</c> value stamped on the replayed message, if applied.
    /// Null if not applied (see <see cref="MarkerApplied"/>). Populated in a later phase.
    /// </summary>
    public string? RecoveryMarker { get; set; }

    /// <summary>
    /// Whether <see cref="RecoveryMarker"/> was actually applied to the replayed message. False
    /// when the provider refused it (e.g. SQS's 10-attribute cap) — never silently dropped.
    /// </summary>
    public bool MarkerApplied { get; set; }

    /// <summary>Current lifecycle state.</summary>
    public RecoveryEntryState State { get; set; } = RecoveryEntryState.Executing;

    /// <summary>Report-friendly terminal outcome category. Null while non-terminal.</summary>
    public RecoveryDisposition? Disposition { get; set; }

    /// <summary>Recurrence-verification outcome. Null until an observation window closes or
    /// this entry never opens one (e.g. a purge).</summary>
    public VerificationResult? VerificationResult { get; set; }

    /// <summary>How a <see cref="VerificationResult.Returned"/> match was attributed.</summary>
    public VerificationConfidence? VerificationConfidence { get; set; }

    /// <summary>When the post-replay observation window closes. Null for entries that never open one.</summary>
    public DateTimeOffset? ObservationWindowEndsAt { get; set; }

    /// <summary>The <see cref="RecoveryEvent.Seq"/> of the most recent event appended for this entry.</summary>
    public long LastEventSeq { get; set; }

    /// <summary>When this entry reached a terminal state. Null while non-terminal.</summary>
    public DateTimeOffset? ClosedAt { get; set; }
}
