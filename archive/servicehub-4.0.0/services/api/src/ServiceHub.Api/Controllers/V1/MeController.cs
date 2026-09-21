using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Exposes the current caller's own identity — primarily so an owner can discover the exact
/// owner ID string a colleague needs to share a namespace with them (see
/// <c>NamespacesController.Share</c>), and, since Governance/RBAC enforcement shipped, their own
/// fleet-wide effective Governance role, for the frontend to decide what to show without a 403
/// round-trip. No scope requirement: any successfully authenticated caller can read their own
/// identity and role regardless of granted scopes.
/// </summary>
[Route(ApiRoutes.Me.Base)]
[Tags("Me")]
public sealed class MeController : ApiControllerBase
{
    private readonly IGovernanceAccessEvaluator _governanceAccessEvaluator;

    /// <summary>Initializes a new instance of the <see cref="MeController"/> class.</summary>
    public MeController(IGovernanceAccessEvaluator governanceAccessEvaluator)
    {
        _governanceAccessEvaluator = governanceAccessEvaluator ?? throw new ArgumentNullException(nameof(governanceAccessEvaluator));
    }

    /// <summary>Returns the caller's own owner ID, how this request authenticated, and their
    /// fleet-wide effective Governance role.</summary>
    /// <response code="200">The caller's identity.</response>
    [HttpGet]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken cancellationToken = default)
    {
        var authMethod = HttpContext.Items.TryGetValue("AuthMethod", out var v) && v is string s ? s : null;

        var governanceRole = await _governanceAccessEvaluator.GetEffectiveRoleAsync(
            OwnerId, ResolveGovernanceGranteeIdentity(), namespaceId: null, pillarKind: null, cancellationToken);

        return Ok(new MeResponse(OwnerId, authMethod, governanceRole?.ToString()));
    }
}
