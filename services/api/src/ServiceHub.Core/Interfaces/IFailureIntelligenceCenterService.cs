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

    /// <summary>
    /// Gets the Incident Center's fleet-wide incident list: every failure signature the owner
    /// has (any lifecycle status), plus fleet-wide metrics, a trend chart, and a category
    /// breakdown. Filtering, sorting, and pagination for display are the caller's
    /// responsibility, mirroring how <c>DlqOverviewPage</c>/<c>SignatureListPage</c> already
    /// handle fleet-sized datasets client-side.
    /// </summary>
    /// <param name="ownerId">Owner for multi-tenant isolation.</param>
    /// <param name="trendDays">Trend window: 1 (hourly buckets), 7, or 30 (daily buckets).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<IncidentListResponse>> GetIncidentsListAsync(
        string ownerId,
        int trendDays,
        CancellationToken cancellationToken = default);
}
