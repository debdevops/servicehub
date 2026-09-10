using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Home as a ranked attention queue (roadmap W2.2) — the three failure signatures across an
/// owner's fleet most worth a human's attention right now, ordered by severity, blast radius,
/// recurrence, and whether a human decision is blocking. Downstream of the W2.1 Incident
/// read-model: this ranks candidates for an incident URL, it does not replace one.
/// </summary>
public interface IAttentionQueueService
{
    /// <summary>Builds the ranked, capped attention queue for one owner across every namespace
    /// they own.</summary>
    /// <param name="ownerId">Tenant/owner identifier for isolation.</param>
    /// <param name="provider">When set, scores and caps the queue within this provider's
    /// namespaces only — e.g. so a cloud-specific Home shows AWS's own top-3, not a post-hoc
    /// filter of the global top-3 that could silently go empty if another cloud's issues
    /// dominated the unfiltered ranking. Null preserves the original cross-cloud behaviour.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<AttentionQueueResponse>> GetAttentionQueueAsync(
        string ownerId,
        CloudProviderType? provider = null,
        CancellationToken cancellationToken = default);
}
