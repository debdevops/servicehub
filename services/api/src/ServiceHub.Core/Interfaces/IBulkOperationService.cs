using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Orchestrates bulk replay/purge operations against DLQ messages matching a filter —
/// "replay these 3,000 messages matching this filter" as a durable, cancellable, resumable-
/// status job rather than a single request/response cycle that can time out on large DLQs.
/// </summary>
/// <remarks>
/// This service owns validation, matching, and job persistence; the actual per-message work is
/// performed by <see cref="IBulkOperationExecutor"/> on a background worker, reusing
/// <see cref="IMessageOperationsService"/> — the same provider-neutral facade single-message
/// replay/purge already goes through. No provider-specific code exists at this layer.
/// </remarks>
public interface IBulkOperationService
{
    /// <summary>
    /// Dry-runs a bulk operation: matches messages against the filter, samples up to 10 of
    /// them, and surfaces safety/capability warnings — without mutating anything.
    /// </summary>
    Task<Result<BulkOperationPreviewResponse>> PreviewAsync(
        string ownerId,
        BulkOperationPreviewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the request (production guard, capability, non-empty match), persists a
    /// <see cref="BulkOperationJob"/> in <see cref="Enums.BulkOperationStatus.Pending"/>, and
    /// hands it to the background worker for processing. Returns the created job immediately —
    /// the caller polls <see cref="GetJobAsync"/> for progress.
    /// </summary>
    Task<Result<BulkOperationJobResponse>> CreateJobAsync(
        string ownerId,
        BulkOperationCreateRequest request,
        string? correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a single job's current state, scoped to the owner.</summary>
    Task<Result<BulkOperationJobResponse>> GetJobAsync(
        string ownerId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists jobs for the owner, most recent first, optionally filtered by namespace.</summary>
    Task<Result<DTOs.Responses.PaginatedResponse<BulkOperationJobResponse>>> ListJobsAsync(
        string ownerId,
        Guid? namespaceId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation of a running or pending job. Idempotent — cancelling an already
    /// terminal job is a no-op success, not an error. Takes effect between messages, not
    /// mid-message.
    /// </summary>
    Task<Result<BulkOperationJobResponse>> CancelJobAsync(
        string ownerId,
        Guid jobId,
        CancellationToken cancellationToken = default);
}
