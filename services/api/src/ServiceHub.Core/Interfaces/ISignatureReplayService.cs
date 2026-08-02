using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Orchestrates replaying every message currently belonging to a failure signature — "replay
/// this whole recurring failure, not just one message" — reusing <see cref="IMessageOperationsService"/>
/// (the same provider-neutral facade single-message and bulk replay already go through) for the
/// actual replay call. Job state is in-memory only (see <see cref="ISignatureReplayJobStore"/>);
/// there is no persisted job row, unlike <see cref="IBulkOperationService"/>.
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
    /// Validates the request (production guard, Send permission, non-empty match), creates an
    /// in-memory job, and starts processing it in the background. Returns the created job
    /// immediately — the caller polls <see cref="GetJobAsync"/> for progress.
    /// </summary>
    Task<Result<BulkOperationJobResponse>> StartAsync(
        string ownerId,
        SignatureReplayStartRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Gets a single job's current state, scoped to the owner.</summary>
    Task<Result<BulkOperationJobResponse>> GetJobAsync(
        string ownerId,
        Guid jobId,
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
