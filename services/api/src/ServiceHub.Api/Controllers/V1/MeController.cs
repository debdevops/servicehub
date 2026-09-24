using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.DTOs.Responses;

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
    public IActionResult Get() => Ok(new MeResponse(OwnerId, AuthMethod, AuditMapping.ToResponse(Actor), EffectiveRole: null));
}
