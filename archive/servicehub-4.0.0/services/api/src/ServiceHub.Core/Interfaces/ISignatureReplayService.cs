using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Orchestrates replaying every message currently belonging to a failure signature — "replay
/// this whole recurring failure, not just one message" — reusing <see cref="IMessageOperationsService"/>
/// (the same provider-neutral facade single-message and bulk replay already go through) for the
/// actual replay call. Job state is persisted (<see cref="Entities.SignatureReplayJob"/>), the
/// same durability shape <see cref="IBulkOperationService"/> already has.
/// </summary>
public interface ISignatureReplayService
{
    /// <summary>
    /// Dry-runs a signature replay: resolves the signature's current member messages, applies
    /// the requested filter, samples up to 10 of the matches, and surfaces safety/capability
    /// warnings — without mutating anything.
    /// </summary>
    Task<Result<BulkOperationPreviewResponse>> PreviewAsync(
        string ownerId,
        SignatureReplayPreviewRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates the request (production guard, Send permission, non-empty match), persists a
    /// <c>Pending</c> job, and enqueues it for the background worker. Returns the created job
    /// immediately — the caller polls <see cref="GetJobAsync"/> for progress.
    /// <paramref name="requestedBy"/> is the requester's actor, resolved by the caller (HTTP
    /// context) while it's still available — persisted on the job so the background worker that
    /// executes it later can attribute ledger writes correctly (roadmap §29.10).
    /// </summary>
    Task<Result<BulkOperationJobResponse>> StartAsync(
        string ownerId,
        SignatureReplayStartRequest request,
        RecoveryActor requestedBy,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a single job's current state, scoped to the owner.</summary>
    Task<Result<BulkOperationJobResponse>> GetJobAsync(
        string ownerId,
        Guid jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists past and in-flight replay jobs for one signature, most recent first — the durable
    /// replay-history record for that signature.
    /// </summary>
    Task<Result<PaginatedResponse<BulkOperationJobResponse>>> ListJobsAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
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
