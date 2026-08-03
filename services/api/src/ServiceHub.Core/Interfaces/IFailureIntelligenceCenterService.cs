using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Aggregates failure signatures, replay jobs, and knowledge gaps into an
/// incident command center view. Computes priority scores, identifies actionable
/// items, and surfaces recent changes across the fleet.
/// </summary>
public interface IFailureIntelligenceCenterService
{
    /// <summary>
    /// Get the investigation center view: investigation queue, failed replays,
    /// knowledge review items, new signatures, and recent changes.
    /// </summary>
    /// <param name="ownerId">Owner for multi-tenant isolation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>InvestigationCenterResponse with all sections.</returns>
    Task<Result<InvestigationCenterResponse>> GetInvestigationCenterAsync(
        string ownerId,
        CancellationToken cancellationToken = default);
}
