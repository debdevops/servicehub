using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Components;
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
    private readonly IComponentOverviewService _components;
    private readonly TimeProvider _time;

    /// <summary>Creates the controller.</summary>
    public FleetController(IFleetOverviewService fleet, IComponentOverviewService components, TimeProvider? time = null)
    {
        _fleet = fleet ?? throw new ArgumentNullException(nameof(fleet));
        _components = components ?? throw new ArgumentNullException(nameof(components));
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

    /// <summary>
    /// What ServiceHub covers (5.0.0): every component on every cloud, where failed work ends up, how it is found, whether it
    /// can be put back, and why not when it cannot. The whole catalogue, the same for every caller: it describes the product,
    /// not this installation. The menu, the coverage page and the README table all come from it.
    /// </summary>
    [HttpGet("coverage")]
    [ProducesResponseType(typeof(CoverageResponse), StatusCodes.Status200OK)]
    public IActionResult Coverage() => Ok(CoverageResponse.Current);

    /// <summary>
    /// Every connected component the caller may see (5.0.0): one row per component of each connection, with its entities, what the
    /// cloud reports, what ServiceHub recorded, and what waits for a person. Scoped to the caller's owner and allow-list exactly as
    /// <see cref="Overview"/> is. <paramref name="cloud"/> is <c>azure</c>, <c>aws</c> or <c>gcp</c>; omitted means all.
    /// </summary>
    [HttpGet("components")]
    [ProducesResponseType(typeof(ComponentOverview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Components([FromQuery] string? cloud, CancellationToken cancellationToken)
    {
        CloudKind? wanted = null;
        if (cloud is not null)
        {
            if (!Enum.TryParse<CloudKind>(cloud, ignoreCase: true, out var parsed) || !Enum.IsDefined(parsed) || cloud.Trim() != cloud || int.TryParse(cloud, out _))
            {
                return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'cloud' must be azure, aws or gcp.");
            }

            wanted = parsed;
        }

        return Ok(await _components.GetAsync(OwnerId, AllowedNamespaceIds, wanted, cancellationToken));
    }

    /// <summary>One connected component with all its entities. 404 when it does not exist or the caller may not see it.</summary>
    [HttpGet("components/{namespaceId:guid}/{componentKey}")]
    [ProducesResponseType(typeof(ConnectedComponent), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Component(Guid namespaceId, string componentKey, CancellationToken cancellationToken)
    {
        var found = await _components.GetOneAsync(OwnerId, AllowedNamespaceIds, namespaceId, componentKey, cancellationToken);
        return found is null
            ? Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, "No such component.")
            : Ok(found);
    }
}
