using Microsoft.AspNetCore.Mvc;
using ServiceHub.Infrastructure.Telemetry;
using ServiceHub.Core.Interfaces;
using ServiceHub.Api.Authorization;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Cross-namespace fleet operations overview — the "what died overnight across everything?"
/// dashboard that turns ServiceHub from a per-namespace debugger into an operations platform.
/// </summary>
[Route(ApiRoutes.Fleet.Base)]
[Tags("Fleet Operations")]
public sealed class FleetController : ApiControllerBase
{
    private readonly IFleetOverviewService _fleetOverviewService;
    private readonly ServiceHubMetrics _metrics;

    /// <summary>Initializes a new instance of the <see cref="FleetController"/> class.</summary>
    public FleetController(IFleetOverviewService fleetOverviewService, ServiceHubMetrics metrics)
    {
        _fleetOverviewService = fleetOverviewService
            ?? throw new ArgumentNullException(nameof(fleetOverviewService));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    /// <summary>
    /// Gets a fleet-wide DLQ overview across all namespaces owned by the caller.
    /// </summary>
    /// <param name="windowHours">The "recent activity" window in hours (default 24, clamped 1–720).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The fleet overview snapshot.</returns>
    [HttpGet("overview")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(FleetOverview), StatusCodes.Status200OK)]
    public async Task<ActionResult<FleetOverview>> GetOverviewAsync(
        [FromQuery] int windowHours = 24,
        CancellationToken cancellationToken = default)
    {
        var result = await _fleetOverviewService.GetOverviewAsync(OwnerId, windowHours, cancellationToken);
        if (result.IsSuccess)
        {
            _metrics.RecordFleetOverview(result.Value.TotalActive, result.Value.NamespaceCount);
        }

        return ToActionResult(result);
    }
}
