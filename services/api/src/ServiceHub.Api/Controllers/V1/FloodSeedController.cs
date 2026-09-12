using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using ServiceHub.Api.Authorization;
using ServiceHub.Infrastructure.Testing;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Local test infrastructure for the high-volume/flood verification pass — never part of the
/// product's normal request surface. Generates realistic <c>Namespace</c>/<c>DlqMessage</c> rows
/// directly against the running SQLite database so DLQ Overview, DLQ Intelligence, Incident
/// Center, Fleet Overview, pagination/filtering/aggregation and bulk operations can be exercised
/// at real volume without provisioning real cloud infrastructure at that scale. Gated to
/// <c>IHostEnvironment.IsDevelopment()</c> in addition to the normal <c>Admin</c> scope
/// check, so it can never be reachable in a production deployment even if an Admin key leaks.
/// </summary>
[Route("api/v1/admin/test/flood-seed")]
[Tags("FloodSeed (test infrastructure)")]
public sealed class FloodSeedController : ApiControllerBase
{
    private readonly FloodSeedService _seedService;
    private readonly IHostEnvironment _environment;

    public FloodSeedController(FloodSeedService seedService, IHostEnvironment environment)
    {
        _seedService = seedService ?? throw new ArgumentNullException(nameof(seedService));
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
    }

    /// <summary>
    /// Generates flood-test namespaces/DLQ messages. Development-only regardless of scope.
    /// </summary>
    [HttpPost]
    [RequireScope(ApiKeyScopes.Admin)]
    public async Task<ActionResult<FloodSeedResult>> Seed(
        [FromBody] FloodSeedRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!_environment.IsDevelopment())
        {
            return NotFound();
        }

        var options = new FloodSeedOptions(
            NamespacesPerProvider: Math.Clamp(request.NamespacesPerProvider, 1, 50),
            MinEntitiesPerNamespace: Math.Clamp(request.MinEntitiesPerNamespace, 0, 500),
            MaxEntitiesPerNamespace: Math.Clamp(request.MaxEntitiesPerNamespace, 1, 500),
            MinDlqPerEntity: Math.Clamp(request.MinDlqPerEntity, 0, 5000),
            MaxDlqPerEntity: Math.Clamp(request.MaxDlqPerEntity, 0, 5000),
            MegaEntitiesPerNamespace: Math.Clamp(request.MegaEntitiesPerNamespace, 0, 10),
            MegaEntityDlqMax: Math.Clamp(request.MegaEntityDlqMax, 0, 5000),
            Seed: request.Seed);

        // Always seeded under the SPA owner, regardless of which credential calls this endpoint —
        // the point is for the browser (SPA) to see the generated fleet, and the SPA always reads
        // as ServiceHub.Core.Entities.Namespace.SpaOwnerId.
        var result = await _seedService.SeedAsync(options, ServiceHub.Core.Entities.Namespace.SpaOwnerId, cancellationToken);
        return Ok(result);
    }
}

/// <summary>Request body for <see cref="FloodSeedController.Seed"/>.</summary>
public sealed record FloodSeedRequest(
    int NamespacesPerProvider = 5,
    int MinEntitiesPerNamespace = 5,
    int MaxEntitiesPerNamespace = 15,
    int MinDlqPerEntity = 0,
    int MaxDlqPerEntity = 50,
    int MegaEntitiesPerNamespace = 0,
    int MegaEntityDlqMax = 0,
    int Seed = 20260912);
