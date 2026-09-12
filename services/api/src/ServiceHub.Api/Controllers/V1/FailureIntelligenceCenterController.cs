using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;
using ServiceHub.Api.Authorization;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Failure Intelligence Center — incident command for investigating and acting on
/// failure signatures. Aggregates investigations, replay failures, knowledge gaps,
/// and recent changes into a single action-focused view.
/// </summary>
[Route(ApiRoutes.FailureIntelligence.Base)]
[Tags("Failure Intelligence")]
public sealed class FailureIntelligenceCenterController : ApiControllerBase
{
    private readonly IFailureIntelligenceCenterService _centerService;

    /// <summary>Initializes a new instance of the <see cref="FailureIntelligenceCenterController"/> class.</summary>
    public FailureIntelligenceCenterController(IFailureIntelligenceCenterService centerService)
    {
        _centerService = centerService ?? throw new ArgumentNullException(nameof(centerService));
    }

    /// <summary>
    /// Gets the Investigation Center view: investigation queue, failed replays,
    /// knowledge review items, new signatures, and recent changes.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The investigation center response with all sections.</returns>
    [HttpGet("investigation-center")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(InvestigationCenterResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<InvestigationCenterResponse>> GetInvestigationCenterAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _centerService.GetInvestigationCenterAsync(OwnerId, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Gets the Incident Center's fleet-wide incident list: every failure signature the owner
    /// has, plus fleet-wide metrics, a trend chart, and a category breakdown.
    /// </summary>
    /// <param name="days">Trend window in days: 1 (hourly buckets), 7, or 30. Defaults to 7.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("incidents")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(IncidentListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<IncidentListResponse>> GetIncidentsListAsync(
        [FromQuery] int days = 7,
        CancellationToken cancellationToken = default)
    {
        var result = await _centerService.GetIncidentsListAsync(OwnerId, days, cancellationToken);
        return ToActionResult(result);
    }
}
