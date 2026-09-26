using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Security;
using ServiceHub.Core.Constants;
using ServiceHub.Core.DTOs.Requests;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Who may do what (unit 5.7): Viewer · Operator · Approver · Admin, fleet-wide or per namespace. Admin only. Adapted from
/// 4.0.0's <c>GovernanceController</c> (grants only — its configuration export/import is not in 4.1.0).
/// </summary>
/// <remarks>
/// <b>Turning governance on keeps everyone who is not restricted.</b> Until the first grant exists, everyone is Admin. The
/// first grant ever also records an owner-level Admin grant — 4.0.0's seeder did the same — so a person restricting one API
/// key does not lock out every other caller. A grant is revoked, never deleted: history is what stops a revoked identity
/// falling back to the owner-level grant (the 2026-09-19 privilege-escalation fix, kept with its tests).
/// </remarks>
[Route("api/v1/governance")]
public sealed class GovernanceController : ApiControllerBase
{
    private readonly IGovernanceGrantService _grants;

    /// <summary>Creates the controller.</summary>
    public GovernanceController(IGovernanceGrantService grants) => _grants = grants ?? throw new ArgumentNullException(nameof(grants));

    /// <summary>The active grants, optionally of one grantee.</summary>
    [HttpGet("grants")]
    [ProducesResponseType(typeof(IReadOnlyList<GovernanceGrantResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List([FromQuery] string? granteeIdentity, CancellationToken cancellationToken)
    {
        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "see who has which role", cancellationToken) is { } denied) return denied;
        var result = string.IsNullOrWhiteSpace(granteeIdentity)
            ? await _grants.GetActiveGrantsAsync(OwnerId, cancellationToken)
            : await _grants.GetGrantsForGranteeAsync(OwnerId, granteeIdentity, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Ok(result.Value.Select(ToResponse).ToList());
    }

    /// <summary>Grants a role. Intent <c>grant-role</c>.</summary>
    [HttpPost("grants")]
    [ProducesResponseType(typeof(GovernanceGrantResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Grant([FromBody] GrantGovernanceRoleRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!IntentHeaders.Declares(Request, IntentHeaders.GrantRole))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("grant a role", IntentHeaders.GrantRole));
        }

        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "grant roles", cancellationToken) is { } denied) return denied;
        if (string.IsNullOrWhiteSpace(request.GranteeIdentity) || request.GranteeIdentity.Length > 256)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "Say who (up to 256 characters): a sign-in name, or an API key's name.");
        }

        var by = Actor.Identity;
        var first = await _grants.HasAnyGrantEverAsync(OwnerId, cancellationToken);
        if (first.IsFailure) return Problem(first.Error);
        if (!first.Value)
        {
            // Turning governance on: everyone not individually restricted keeps Admin (4.0.0's seeder rule).
            var seed = await _grants.GrantAsync(new GrantRoleRequest(OwnerId, OwnerId, GranteeKind.User, GovernanceRole.Admin, null, null, by), cancellationToken);
            if (seed.IsFailure) return Problem(seed.Error);
        }

        var result = await _grants.GrantAsync(
            new GrantRoleRequest(OwnerId, request.GranteeIdentity.Trim(), request.GranteeKind, request.Role, request.NamespaceId, request.PillarKind, by), cancellationToken);
        return result.IsFailure ? Problem(result.Error) : StatusCode(StatusCodes.Status201Created, ToResponse(result.Value));
    }

    /// <summary>Revokes a grant (it stays in history). Intent <c>revoke-role</c>.</summary>
    [HttpPost("grants/{id:guid}/revoke")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status428PreconditionRequired)]
    public async Task<IActionResult> Revoke(Guid id, CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.RevokeRole))
        {
            return Problem(StatusCodes.Status428PreconditionRequired, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("revoke a role", IntentHeaders.RevokeRole));
        }

        if (await DeniedUnlessAsync(GovernanceRole.Admin, null, null, "revoke roles", cancellationToken) is { } denied) return denied;
        var result = await _grants.RevokeAsync(id, OwnerId, Actor.Identity, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : NoContent();
    }

    private static GovernanceGrantResponse ToResponse(GovernanceGrant g) => new(
        g.Id, g.GranteeIdentity, g.GranteeKind.ToString(), g.Role.ToString(), g.NamespaceId, g.PillarKind?.ToString(),
        g.GrantedAt, g.GrantedByIdentity, g.RevokedAt, g.RevokedByIdentity);
}
