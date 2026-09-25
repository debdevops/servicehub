using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>Failure signatures (unit 3.8): which failures are the same failure. Read-only — a rule is created on Auto Replay, never here.</summary>
[Route("api/v1/signatures")]
public sealed class SignaturesController : ApiControllerBase
{
    private readonly ISignaturesService _signatures;

    /// <summary>Creates the controller.</summary>
    public SignaturesController(ISignaturesService signatures) => _signatures = signatures ?? throw new ArgumentNullException(nameof(signatures));

    /// <summary>A page of signatures. <paramref name="tab"/> is all (default), growing, helps or doesnt.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(SignaturePage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] CloudProviderType? provider, [FromQuery] int? days, [FromQuery] string? tab, [FromQuery] string? sort,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        if (tab is not (null or "all" or "growing" or "helps" or "doesnt"))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'tab' must be all, growing, helps or doesnt.");
        }

        return Ok(await _signatures.ListAsync(OwnerId, AllowedNamespaceIds, provider, days ?? 7, tab, sort ?? "messages", page ?? 1, pageSize ?? 25, cancellationToken));
    }

    /// <summary>One signature.</summary>
    [HttpGet("{hash}")]
    [ProducesResponseType(typeof(SignatureSummary), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string hash, [FromQuery] CloudProviderType provider, [FromQuery] int? days, CancellationToken cancellationToken)
    {
        var found = await _signatures.GetAsync(OwnerId, AllowedNamespaceIds, hash, provider, days ?? 7, cancellationToken);
        return found is null
            ? Problem(StatusCodes.Status404NotFound, ErrorCodes.NotFound, $"No signature '{hash}' was found.")
            : Ok(found);
    }
}
