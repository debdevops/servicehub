using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>The durable history Home's Recent Activity reads.</summary>
[Route("api/v1/audit")]
public sealed class AuditController : ApiControllerBase
{
    private readonly IAuditTrail _audit;

    /// <summary>Creates the controller.</summary>
    public AuditController(IAuditTrail audit)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <summary>
    /// History for this caller, newest first. Scoped by owner and by the caller's namespace
    /// allow-list — a restricted caller cannot read, or count, entries about namespaces it cannot see.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AuditPageResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] string? action = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _audit.QueryAsync(
            new AuditQuery(OwnerId, AllowedNamespaceIds, namespaceId, action, page, pageSize), cancellationToken);

        return Ok(new AuditPageResponse(
            result.Items.Select(AuditMapping.ToResponse).ToList(),
            Math.Max(page, 1),
            Math.Clamp(pageSize, 1, 200),
            result.Total));
    }
}
