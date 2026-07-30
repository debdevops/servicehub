using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Exposes the current caller's own identity — primarily so an owner can discover the exact
/// owner ID string a colleague needs to share a namespace with them (see
/// <c>NamespacesController.Share</c>). No scope requirement: any successfully authenticated
/// caller can read their own identity regardless of granted scopes.
/// </summary>
[Route(ApiRoutes.Me.Base)]
[Tags("Me")]
public sealed class MeController : ApiControllerBase
{
    /// <summary>Returns the caller's own owner ID and how this request authenticated.</summary>
    /// <response code="200">The caller's identity.</response>
    [HttpGet]
    [ProducesResponseType(typeof(MeResponse), StatusCodes.Status200OK)]
    public ActionResult<MeResponse> Get()
    {
        var authMethod = HttpContext.Items.TryGetValue("AuthMethod", out var v) && v is string s ? s : null;
        return Ok(new MeResponse(OwnerId, authMethod));
    }
}
