using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// A durable, resumable-in-status record of a bulk replay or bulk purge operation against
/// every DLQ message matching a filter — "replay these 3,000 messages matching this filter,"
/// tracked from creation through completion so the caller can poll progress and cancel.
/// </summary>
/// <remarks>
/// This is deliberately a thin orchestration record: it stores the filter that selected the
/// messages and running counters, not a per-message row. Per-message audit trail for replays
/// continues to flow through the existing <see cref="ReplayHistory"/> table (one row per
/// message, exactly as single-message replay already writes); purge has no equivalent
/// per-message table today; a bounded sample of failures is kept on <see cref="FailureSampleJson"/>
/// for troubleshooting without unbounded per-item storage growth.
/// </remarks>
public sealed class BulkOperationJob
{
    /// <summary>Primary key.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Owner ID for multi-user isolation. Same format as <see cref="DlqMessage.OwnerId"/>.
    /// </summary>
    public required string OwnerId { get; init; }

    // ── Requester actor snapshot (roadmap §29.10) ──────────────────────────────
    // Persisted at job-creation time, when the requester's real identity is still available from
    // HttpContext, so the background worker that executes the job later (no HttpContext) can
    // attribute every ledger write to the human/API-key who actually asked for it instead of
    // falling back to a synthetic Automation actor. Execution mechanism is not the actor of
    // record — see BulkOperationExecutor.RunAsync.

    /// <summary>The requester's resolved actor identity, e.g. <c>ApiKey:ops-bot</c> or
    /// <c>user@example.com</c> — same value <see cref="Models.RecoveryActor.Identity"/> carries.</summary>
    public required string RequestedByIdentity { get; init; }

    /// <summary>The requester's actor kind, as resolved by <c>ApiControllerBase.ResolveRecoveryActor</c>
    /// at job-creation time — always <c>User</c> or <c>ApiKey</c> for an HTTP-created job.</summary>
    public required RecoveryActorKind RequestedByActorKind { get; init; }

    /// <summary>The requester's granted scopes at job-creation time, for API key actors.</summary>
    public string? RequestedByScopes { get; init; }

    /// <summary>Whether this job replays or purges matched messages.</summary>
    public required BulkOperationType OperationType { get; init; }

    /// <summary>Current lifecycle status.</summary>
    public BulkOperationStatus Status { get; set; } = BulkOperationStatus.Pending;

    // ── Filter snapshot (same shape as IDlqHistoryService.GetHistoryAsync) ─────

    /// <summary>The namespace this job operates within. Bulk operations are single-namespace by design — see FLOW.md.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Display name of the namespace, snapshotted at creation for history views after the namespace may be renamed/deleted.</summary>
    public required string NamespaceDisplayName { get; init; }

    /// <summary>Optional entity-name filter (substring match, same as the DLQ History page).</summary>
    public string? EntityNameFilter { get; init; }

    /// <summary>Optional status filter — only messages in this <see cref="DlqMessageStatus"/> are matched.</summary>
    public DlqMessageStatus? StatusFilter { get; init; }

    /// <summary>Optional failure-category filter.</summary>
    public FailureCategory? CategoryFilter { get; init; }

    /// <summary>Optional inclusive lower bound on <see cref="DlqMessage.DetectedAtUtc"/>.</summary>
    public DateTimeOffset? FromFilter { get; init; }

    /// <summary>Optional inclusive upper bound on <see cref="DlqMessage.DetectedAtUtc"/>.</summary>
    public DateTimeOffset? ToFilter { get; init; }

    // ── Progress ─────────────────────────────────────────────────────────────

    /// <summary>Total number of messages matched by the filter at job-creation time. Fixed once set — the job does not pick up messages detected after it started.</summary>
    public int TotalMatched { get; set; }

    /// <summary>Number of matched messages the worker has finished attempting (success, failure, or skip).</summary>
    public int ProcessedCount { get; set; }

    /// <summary>Number of messages successfully replayed/purged.</summary>
    public int SuccessCount { get; set; }

    /// <summary>Number of messages that were attempted and failed.</summary>
    public int FailureCount { get; set; }

    /// <summary>Number of messages skipped without an attempt (e.g. provider doesn't support purge, namespace no longer exists).</summary>
    public int SkippedCount { get; set; }

    /// <summary>JSON array of up to 20 <c>{ messageId, entityName, reason }</c> objects describing the most recent failures/skips, for troubleshooting without a per-item table.</summary>
    public string? FailureSampleJson { get; set; }

    /// <summary>Top-level error if the job could not run at all (e.g. namespace deleted between creation and start).</summary>
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
