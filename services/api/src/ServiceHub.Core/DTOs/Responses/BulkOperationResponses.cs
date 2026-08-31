namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Response for <c>POST /api/v1/bulk-operations/preview</c> — a dry run that selects and
/// samples the matching messages without mutating anything, so the caller can see exactly
/// what a bulk replay/purge would do before committing to it.
/// </summary>
/// <param name="TotalMatched">Total messages the filter currently matches.</param>
/// <param name="Sample">Up to 10 matched messages, for a concrete "here's what you're about to touch" preview.</param>
/// <param name="CanExecute">Whether creating the job would be accepted — false when the namespace is production, the filter matches zero messages, or (for replay) the namespace lacks Send permission.</param>
/// <param name="Warnings">Human-readable caveats surfaced before execution (production block, provider capability gaps, replay-safety concerns).</param>
/// <param name="UnsafeReplayCount">Of the matched messages, how many have <c>ReplaySafety == "Unsafe"</c> (replay preview only; always 0 for purge).</param>
/// <remarks>
/// There is no per-message "skipped by capability" count here: capability
/// (<see cref="Models.ProviderCapabilities"/>) is a fact about the namespace's provider as a
/// whole, not about individual messages, so an unsupported operation blocks the entire job
/// (<see cref="CanExecute"/> is <see langword="false"/> with an explanatory warning) rather than
/// silently skipping a subset.
/// </remarks>
public sealed record BulkOperationPreviewResponse(
    int TotalMatched,
    IReadOnlyList<BulkOperationPreviewItem> Sample,
    bool CanExecute,
    IReadOnlyList<string> Warnings,
    int UnsafeReplayCount);

/// <summary>One sampled message in a <see cref="BulkOperationPreviewResponse"/>.</summary>
public sealed record BulkOperationPreviewItem(
    long DlqMessageId,
    string MessageId,
    string EntityName,
    string? DeadLetterReason,
    string? ReplaySafety);

/// <summary>
/// A single message outcome captured in <see cref="BulkOperationJobResponse.FailureSample"/>.
/// </summary>
public sealed record BulkOperationFailureSample(
    string MessageId,
    string EntityName,
    string Reason,
    // The provider-agnostic bucket the underlying provider error classifies into (see
    // ServiceHub.Core.Enums.ReplayFailureReason), as its string name — e.g. "NotFound",
    // "Retryable", "ProviderError". Null for outcomes not classified this way (currently only
    // populated by signature replay; plain "Skipped" outcomes carry no provider Error to
    // classify).
    string? ReasonCategory = null);

/// <summary>
/// The full state of a bulk operation job — returned by create, get, and list endpoints so the
/// frontend can poll a single shape throughout the job's lifecycle.
/// </summary>
public sealed record BulkOperationJobResponse(
    Guid Id,
    string OperationType,
    string Status,
    Guid NamespaceId,
    string NamespaceDisplayName,
    string? EntityNameFilter,
    string? StatusFilter,
    string? CategoryFilter,
    DateTimeOffset? From,
    DateTimeOffset? To,
    int TotalMatched,
    int ProcessedCount,
    int SuccessCount,
    int FailureCount,
    int SkippedCount,
    IReadOnlyList<BulkOperationFailureSample>? FailureSample,
    string? ErrorSummary,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    bool IsCancellable,
    // While Status is Pending: how many other jobs (globally, across the shared single-concurrency
    // worker) must finish before this one starts — 0 means "next up, worker will pick it up as soon
    // as it's free." Null once the job is Running or terminal, or for operation types not served by
    // a single-concurrency worker. Exists so a long queue wait behind an unrelated slow job (e.g.
    // AWS's per-message DLQ scan) reads as "waiting behind N job(s)" instead of an unexplained,
    // indefinite "Queued" spinner.
    int? QueueAheadCount = null);
