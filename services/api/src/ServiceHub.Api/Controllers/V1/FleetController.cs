using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// The only cross-cloud view (unit 3.5): every connected cloud side by side. Read-only. Each cloud keeps its own
/// numbers — nothing here is added across clouds, and no provider is named (rule R4).
/// </summary>
[Route("api/v1/fleet")]
public sealed class FleetController : ApiControllerBase
{
    private readonly IFleetOverviewService _fleet;
    private readonly TimeProvider _time;

    /// <summary>Creates the controller.</summary>
    public FleetController(IFleetOverviewService fleet, TimeProvider? time = null)
    {
        _fleet = fleet ?? throw new ArgumentNullException(nameof(fleet));
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The fleet. <paramref name="window"/> is <c>today</c> (default, since local-UTC midnight), <c>24h</c> or <c>7d</c>.</summary>
    [HttpGet("overview")]
    [ProducesResponseType(typeof(FleetOverview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Overview([FromQuery] string? window, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow();
        var (name, since) = (window ?? "today") switch
        {
            "today" => ("today", new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero)),
            "24h" => ("24h", now.AddHours(-24)),
            "7d" => ("7d", now.AddDays(-7)),
            _ => (null, default),
        };
        if (name is null)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'window' must be today, 24h or 7d.");
        }

        return Ok(await _fleet.GetAsync(OwnerId, AllowedNamespaceIds, name, since, cancellationToken));
    }
}
