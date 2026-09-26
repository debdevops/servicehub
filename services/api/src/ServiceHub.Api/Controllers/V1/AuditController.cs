using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Constants;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>The durable history Home's Recent Activity reads.</summary>
[Route("api/v1/audit")]
public sealed class AuditController : ApiControllerBase
{
    private readonly IAuditTrail _audit;
    private readonly INamespaceRepository _namespaces;

    /// <summary>Creates the controller.</summary>
    public AuditController(IAuditTrail audit, INamespaceRepository namespaces)
    {
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
    }

    /// <summary>
    /// History for this caller, newest first. Optionally narrowed to one cloud and/or environment. Scoped by owner and by the caller's namespace
    /// allow-list — a restricted caller cannot read, or count, entries about namespaces it cannot see.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(AuditPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] string? action = null,
        [FromQuery] CloudProviderType? provider = null,
        [FromQuery] EnvironmentType? environment = null,
        CancellationToken cancellationToken = default)
    {
        if (page < 1 || pageSize is < 1 or > 200)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'page' starts at 1 and 'pageSize' must be between 1 and 200.");
        }

        // A cloud or an environment is a set of namespaces; the audit trail only knows namespaces.
        HashSet<Guid>? scope = null;
        if (provider is not null || environment is not null)
        {
            var visible = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
            if (visible.IsFailure)
            {
                return Problem(visible.Error);
            }

            scope = [.. visible.Value
                .Where(n => (provider is null || n.Provider == provider) && (environment is null || n.Environment == environment))
                .Select(n => n.Id)];
        }

        var result = await _audit.QueryAsync(
            new AuditQuery(OwnerId, AllowedNamespaceIds, namespaceId, action, page, pageSize, scope), cancellationToken);

        return Ok(new AuditPageResponse(
            result.Items.Select(AuditMapping.ToResponse).ToList(),
            Math.Max(page, 1),
            Math.Clamp(pageSize, 1, 200),
            result.Total));
    }
}
