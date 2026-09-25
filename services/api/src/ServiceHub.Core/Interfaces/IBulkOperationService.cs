using ServiceHub.Core.Models;
using ServiceHub.Core.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>Bulk replay (unit 3.2): preview → start → progress → cancel. Every message goes through the eligibility gate on its own.</summary>
public interface IBulkOperationService
{
    /// <summary>Computes and stores a preview for the chosen dead letters. Nothing is sent.</summary>
    Task<Result<BulkPreview>> PreviewAsync(string ownerId, IReadOnlySet<Guid>? allowed, RecoveryActor actor, IReadOnlyList<long> dlqMessageIds, CancellationToken ct);

    /// <summary>Starts the run from a stored preview. The only way a job runs.</summary>
    Task<Result<BulkProgress>> StartAsync(string ownerId, Guid previewId, bool sampleOnly, CancellationToken ct);

    /// <summary>Where a job is.</summary>
    Task<Result<BulkProgress>> GetAsync(string ownerId, Guid id, CancellationToken ct);

    /// <summary>Asks a running job to stop before its next message.</summary>
    Task<Result<BulkProgress>> CancelAsync(string ownerId, Guid id, CancellationToken ct);
}
