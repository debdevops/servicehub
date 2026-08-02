using System.Collections.Concurrent;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.SignatureReplay;

/// <summary>
/// <inheritdoc cref="ISignatureReplayJobStore"/>
/// </summary>
public sealed class SignatureReplayJobStore : ISignatureReplayJobStore
{
    private readonly ConcurrentDictionary<Guid, SignatureReplayJobState> _jobs = new();

    /// <inheritdoc />
    public void Add(SignatureReplayJobState job) => _jobs[job.Id] = job;

    /// <inheritdoc />
    public SignatureReplayJobState? Get(Guid jobId, string ownerId) =>
        _jobs.TryGetValue(jobId, out var job) && string.Equals(job.OwnerId, ownerId, StringComparison.Ordinal)
            ? job
            : null;

    /// <inheritdoc />
    public void RequestCancellation(Guid jobId, string ownerId)
    {
        var job = Get(jobId, ownerId);
        if (job is null)
            return;

        lock (job.SyncRoot)
        {
            if (job.Status is not (BulkOperationStatus.Pending or BulkOperationStatus.Running))
                return;

            job.CancellationRequestedAt = DateTimeOffset.UtcNow;
        }

        job.CancellationTokenSource.Cancel();
    }
}
