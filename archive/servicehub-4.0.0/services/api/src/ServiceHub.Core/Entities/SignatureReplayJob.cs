using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// A durable, resumable-in-status record of replaying every DLQ message currently belonging to
/// a failure signature — "replay this whole recurring failure," tracked from creation through
/// completion so the caller can poll progress, cancel, and later list history. Field-for-field
/// mirrors <see cref="BulkOperationJob"/>; the one addition is <see cref="MessageIdsJson"/>,
/// since a failure signature's member messages are resolved by clustering (not durable on their
/// own) and must be snapshotted at job-creation time to survive a restart.
/// </summary>
public sealed class SignatureReplayJob
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Owner ID for multi-user isolation. Same format as <see cref="DlqMessage.OwnerId"/>.</summary>
    public required string OwnerId { get; init; }

    // ── Requester actor snapshot (roadmap §29.10) ──────────────────────────────
    // See BulkOperationJob's identical fields for the full rationale: persisted at job-creation
    // time so the background worker that executes the job later (no HttpContext) attributes
    // ledger writes to the requesting human/API-key, not a synthetic Automation actor.

    /// <summary>The requester's resolved actor identity — same value <see cref="Models.RecoveryActor.Identity"/> carries.</summary>
    public required string RequestedByIdentity { get; init; }

    /// <summary>The requester's actor kind, as resolved by <c>ApiControllerBase.ResolveRecoveryActor</c>
    /// at job-creation time — always <c>User</c> or <c>ApiKey</c> for an HTTP-created job.</summary>
    public required RecoveryActorKind RequestedByActorKind { get; init; }

    /// <summary>The requester's granted scopes at job-creation time, for API key actors.</summary>
    public string? RequestedByScopes { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public BulkOperationStatus Status { get; set; } = BulkOperationStatus.Pending;

    /// <summary>The namespace this job operates within.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Display name of the namespace, snapshotted at creation.</summary>
    public required string NamespaceDisplayName { get; init; }

    /// <summary>The signature's stable identity hash.</summary>
    public required string SignatureHash { get; init; }

    /// <summary>Optional status filter applied when resolving member messages.</summary>
    public DlqMessageStatus? StatusFilter { get; init; }

    /// <summary>Optional inclusive lower bound on <see cref="DlqMessage.DetectedAtUtc"/>.</summary>
    public DateTimeOffset? FromFilter { get; init; }

    /// <summary>Optional inclusive upper bound on <see cref="DlqMessage.DetectedAtUtc"/>.</summary>
    public DateTimeOffset? ToFilter { get; init; }

    /// <summary>
    /// JSON array of the DLQ message IDs resolved and filtered at job-creation time — the exact
    /// set the executor replays. Snapshotted because signature clustering isn't durable, so
    /// re-resolving membership at execution time could drift from what was previewed/approved.
    /// </summary>
    public required string MessageIdsJson { get; init; }

    // ── Progress ─────────────────────────────────────────────────────────────

    /// <summary>Total number of messages matched at job-creation time. Fixed once set.</summary>
    public int TotalMatched { get; set; }

    /// <summary>Number of matched messages processed so far (success, failure, or skip).</summary>
    public int ProcessedCount { get; set; }

    /// <summary>Number of messages successfully replayed.</summary>
    public int SuccessCount { get; set; }

    /// <summary>Number of messages that were attempted and failed.</summary>
    public int FailureCount { get; set; }

    /// <summary>Number of messages skipped without an attempt.</summary>
    public int SkippedCount { get; set; }

    /// <summary>JSON array of up to 20 <c>{ messageId, entityName, reason }</c> objects describing the most recent failures/skips.</summary>
    public string? FailureSampleJson { get; set; }

    /// <summary>Top-level error if the job could not run at all.</summary>
    public string? ErrorSummary { get; set; }

    // ── Timestamps ───────────────────────────────────────────────────────────

    /// <summary>When the job was created (still <see cref="BulkOperationStatus.Pending"/>).</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the worker began processing.</summary>
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>When the job reached a terminal status.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Set when the caller requests cancellation; the worker checks this between messages.</summary>
    public DateTimeOffset? CancellationRequestedAt { get; set; }

    /// <summary>Correlation ID from the HTTP request that created the job, for cross-referencing logs.</summary>
    public string? CorrelationId { get; init; }
}
