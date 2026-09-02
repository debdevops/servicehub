using ServiceHub.Core.DTOs.Responses;
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
    Task<Result<AttentionQueueResponse>> GetAttentionQueueAsync(
        string ownerId,
        CancellationToken cancellationToken = default);
}
