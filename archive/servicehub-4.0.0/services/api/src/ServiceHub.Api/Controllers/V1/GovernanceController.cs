using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;
using ServiceHub.Shared.Results;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Governance/RBAC grant management (M3 of the persistence wave, roadmap item 10's enforcement
/// layer) — the admin surface for granting/revoking the per-owner, per-namespace, per-pillar
/// roles <see cref="ServiceHub.Api.Filters.GovernanceAuthorizationFilter"/> reads at request time.
/// Every write goes through <see cref="IGovernanceGrantService"/>; this controller only translates
/// HTTP concerns and resolves the acting identity server-side.
/// </summary>
[Route(ApiRoutes.Governance.Base)]
[Tags("Governance")]
[RequireScope(ApiKeyScopes.Admin)]
[RequireGovernanceRole(GovernanceRole.Admin)]
public sealed class GovernanceController : ApiControllerBase
{
    private readonly IGovernanceGrantService _governanceGrantService;
    private readonly IConfigurationExportService _configurationExportService;

    /// <summary>Initializes a new instance of the <see cref="GovernanceController"/> class.</summary>
    public GovernanceController(
        IGovernanceGrantService governanceGrantService,
        IConfigurationExportService configurationExportService)
    {
        _governanceGrantService = governanceGrantService ?? throw new ArgumentNullException(nameof(governanceGrantService));
        _configurationExportService = configurationExportService ?? throw new ArgumentNullException(nameof(configurationExportService));
    }

    /// <summary>
    /// Lists the caller's active Governance grants, optionally narrowed to one grantee identity.
    /// </summary>
    /// <param name="granteeIdentity">Optional exact grantee identity filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("grants")]
    [ProducesResponseType(typeof(IReadOnlyList<GovernanceGrantResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<GovernanceGrantResponse>>> GetGrants(
        [FromQuery] string? granteeIdentity = null,
        CancellationToken cancellationToken = default)
    {
        var result = string.IsNullOrWhiteSpace(granteeIdentity)
            ? await _governanceGrantService.GetActiveGrantsAsync(OwnerId, cancellationToken)
            : await _governanceGrantService.GetGrantsForGranteeAsync(OwnerId, granteeIdentity, cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<IReadOnlyList<GovernanceGrantResponse>>(result.Error);
        }

        return Ok(result.Value.Select(MapToResponse).ToList());
    }

    /// <summary>
    /// Grants a Governance role to an identity, scoped to an optional namespace and/or pillar.
    /// </summary>
    /// <param name="request">The role, grantee, and scope to grant.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("grants")]
    [ProducesResponseType(typeof(GovernanceGrantResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<GovernanceGrantResponse>> Grant(
        [FromBody] GrantGovernanceRoleRequest request,
        CancellationToken cancellationToken = default)
    {
        var grantedByIdentity = ResolveGovernanceGranteeIdentity();

        var result = await _governanceGrantService.GrantAsync(
            new GrantRoleRequest(
                OwnerId,
                request.GranteeIdentity,
                request.GranteeKind,
                request.Role,
                request.NamespaceId,
                request.PillarKind,
                grantedByIdentity),
            cancellationToken);

        if (result.IsFailure)
        {
            return ToActionResult<GovernanceGrantResponse>(result.Error);
        }

        return ToCreatedResult(
            Result.Success(MapToResponse(result.Value)),
            nameof(GetGrants));
    }

    /// <summary>Revokes one Governance grant by ID.</summary>
    /// <param name="id">The grant to revoke.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("grants/{id:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken = default)
    {
        var revokedByIdentity = ResolveGovernanceGranteeIdentity();
        var result = await _governanceGrantService.RevokeAsync(id, OwnerId, revokedByIdentity, cancellationToken);
        return ToActionResult(result);
    }

    /// <summary>
    /// Exports every <c>AutoReplayRule</c> and active Governance grant for the caller's owner as
    /// configuration-as-code (roadmap next-chapter M5.4) — meant to be committed to git and
    /// reviewed via pull request. Never includes a namespace connection string or any ledger
    /// event/finding — see <see cref="IConfigurationExportService"/>'s own remarks.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("configuration/export")]
    [ProducesResponseType(typeof(ConfigurationBundle), StatusCodes.Status200OK)]
    public async Task<ActionResult<ConfigurationBundle>> ExportConfiguration(CancellationToken cancellationToken = default)
    {
        var bundle = await _configurationExportService.ExportAsync(OwnerId, cancellationToken);
        return Ok(bundle);
    }

    /// <summary>
    /// Applies a previously-exported (and possibly hand-edited) configuration bundle back to the
    /// caller's owner. Additive/upsert only — a rule already present (matched by name) is updated
    /// in place, an already-active grant is left alone, and nothing present live but absent from
    /// the bundle is ever deleted or revoked.
    /// </summary>
    /// <param name="bundle">The bundle to import, typically a previously exported one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPost("configuration/import")]
    [ProducesResponseType(typeof(ConfigurationImportResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<ConfigurationImportResult>> ImportConfiguration(
        [FromBody] ConfigurationBundle bundle, CancellationToken cancellationToken = default)
    {
        var actor = ResolveRecoveryActor();
        var result = await _configurationExportService.ImportAsync(OwnerId, bundle, actor, cancellationToken);
        return ToActionResult<ConfigurationImportResult>(result);
    }

    private static GovernanceGrantResponse MapToResponse(GovernanceGrant grant) => new(
        Id: grant.Id,
        GranteeIdentity: grant.GranteeIdentity,
        GranteeKind: grant.GranteeKind.ToString(),
        Role: grant.Role.ToString(),
        NamespaceId: grant.NamespaceId,
        PillarKind: grant.PillarKind?.ToString(),
        GrantedAt: grant.GrantedAt,
        GrantedByIdentity: grant.GrantedByIdentity,
        RevokedAt: grant.RevokedAt,
        RevokedByIdentity: grant.RevokedByIdentity);
}
