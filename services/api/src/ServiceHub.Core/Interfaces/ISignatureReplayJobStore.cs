using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// In-process store for signature-replay job state. Unlike <see cref="IBulkOperationQueue"/> +
/// <see cref="Entities.BulkOperationJob"/>, there is no persisted row backing a signature-replay
/// job — a failure signature's member messages aren't durable anywhere (see
/// <see cref="IDlqSignatureAnalysisService"/>'s doc comment), so persisting a job that targets
/// them would need a new column/table, which is out of scope for this feature. State here is
/// therefore process-lifetime only, the same accepted limitation <see cref="ISignatureLifecycleService"/>
/// already documents for signature lifecycle state: a restart loses in-flight/completed job
/// records, but the underlying DLQ messages themselves are unaffected (each message's status is
/// persisted as it is replayed, independent of this in-memory job record).
/// </summary>
public interface ISignatureReplayJobStore
{
    /// <summary>Registers a newly created job. The job's <see cref="SignatureReplayJobState.Id"/> must be unique.</summary>
    void Add(SignatureReplayJobState job);

    /// <summary>Gets a job by ID, scoped to the owner. Returns <see langword="null"/> if not found or owned by someone else.</summary>
    SignatureReplayJobState? Get(Guid jobId, string ownerId);

    /// <summary>
    /// Requests cancellation of a pending/running job, scoped to the owner. A no-op if the job
    /// doesn't exist, isn't owned by <paramref name="ownerId"/>, or is already terminal.
    /// </summary>
    void RequestCancellation(Guid jobId, string ownerId);
}

/// <summary>
/// Mutable, process-lifetime state for one signature-replay job. Field-for-field mirrors what
/// <see cref="BulkOperationJobResponse"/> needs so the same response DTO (and, on the frontend,
/// the same TypeScript type) serves both bulk-operation jobs and signature-replay jobs.
/// </summary>
public sealed class SignatureReplayJobState
{
    /// <summary>Guards multi-field progress updates from the executor against concurrent reads.</summary>
    public object SyncRoot { get; } = new();

    public required Guid Id { get; init; }
    public required string OwnerId { get; init; }
    public required Guid NamespaceId { get; init; }
    public required string NamespaceDisplayName { get; init; }
    public required string SignatureHash { get; init; }
    public DlqMessageStatus? StatusFilter { get; init; }
    public DateTimeOffset? FromFilter { get; init; }
    public DateTimeOffset? ToFilter { get; init; }

    /// <summary>The DLQ message IDs resolved and filtered at job-creation time — the exact set the executor replays.</summary>
    public required IReadOnlyList<long> MessageIds { get; init; }

    public BulkOperationStatus Status { get; set; } = BulkOperationStatus.Pending;
    public int TotalMatched { get; init; }
    public int ProcessedCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int SkippedCount { get; set; }
    public List<BulkOperationFailureSample> FailureSample { get; } = [];
    public string? ErrorSummary { get; set; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? CancellationRequestedAt { get; set; }
    public required CancellationTokenSource CancellationTokenSource { get; init; }

    public bool IsCancellable => Status is BulkOperationStatus.Pending or BulkOperationStatus.Running;
}
