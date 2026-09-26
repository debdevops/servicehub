using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>Who ServiceHub believes is asking. Never fails because nothing is configured.</summary>
[Route("api/v1/me")]
public sealed class MeController : ApiControllerBase
{
    /// <summary>
    /// The caller's owner, how they authenticated, and how the audit trail will name them. With no
    /// identity configured — a valid deployment — this answers "a browser session", not an error.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        // The fleet-wide role (a namespace grant can add to it, never take away). Admin while governance is inactive.
        var active = (await HttpContext.RequestServices.GetRequiredService<IGovernanceGrantService>().HasAnyGrantEverAsync(OwnerId, cancellationToken)) is { IsSuccess: true, Value: true };
        var role = await HttpContext.RequestServices.GetRequiredService<IGovernanceAccessEvaluator>()
            .GetEffectiveRoleAsync(OwnerId, Actor.Identity, null, null, cancellationToken);
        var evaluator = HttpContext.RequestServices.GetRequiredService<IGovernanceAccessEvaluator>();
        var identity = Actor.Identity;
        var recover = await evaluator.GetEffectiveRoleAsync(OwnerId, identity, null, Core.Enums.PillarKind.Recover, cancellationToken);
        var perNamespace = new Dictionary<Guid, string?>();
        var visible = await HttpContext.RequestServices.GetRequiredService<INamespaceRepository>().GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        foreach (var ns in visible.IsSuccess ? visible.Value : [])
        {
            perNamespace[ns.Id] = (await evaluator.GetEffectiveRoleAsync(OwnerId, identity, ns.Id, Core.Enums.PillarKind.Recover, cancellationToken))?.ToString();
        }

        return Ok(new MeResponse(OwnerId, AuthMethod, AuditMapping.ToResponse(Actor), role?.ToString(), active, await GrantorsAsync(null, cancellationToken),
            recover?.ToString(), perNamespace));
    }
}
