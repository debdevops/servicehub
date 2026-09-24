using Microsoft.AspNetCore.Mvc;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// What has been replayed, newest first — the Replayed tab (unit 2.7). Read-only, and scoped to the
/// namespaces the caller may see. There is no replay screen: replay itself happens in the message drawer.
/// </summary>
[Route("api/v1/replays")]
public sealed class ReplaysController : ApiControllerBase
{
    private readonly INamespaceRepository _namespaces;
    private readonly IDlqReplayService _replay;

    /// <summary>Creates the controller.</summary>
    public ReplaysController(INamespaceRepository namespaces, IDlqReplayService replay)
    {
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _replay = replay ?? throw new ArgumentNullException(nameof(replay));
    }

    /// <summary>Lists replays.</summary>
    /// <param name="namespaceId">One namespace.</param>
    /// <param name="provider">One cloud — every namespace the caller has on it.</param>
    /// <param name="result">Only this outcome: accepted, rejected or unknown.</param>
    /// <param name="dlqMessageId">Only the replays of this one dead letter (the drawer's watch card).</param>
    /// <param name="page">1-based.</param>
    /// <param name="pageSize">1–100 (default 25).</param>
    /// <param name="cancellationToken">Cancellation.</param>
    [HttpGet]
    [ProducesResponseType(typeof(ReplayPage), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> List(
        [FromQuery] Guid? namespaceId, [FromQuery] CloudProviderType? provider, [FromQuery] string? result, [FromQuery] long? dlqMessageId,
        [FromQuery] int? page, [FromQuery] int? pageSize, CancellationToken cancellationToken)
    {
        if (namespaceId is null && provider is null && dlqMessageId is null)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed,
                "Say which replays: give a 'namespaceId', a 'provider', or a 'dlqMessageId'.");
        }

        if (page is < 1 || pageSize is < 1 or > 100)
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'page' starts at 1 and 'pageSize' must be between 1 and 100.");
        }

        if (result is not (null or "" or "accepted" or "rejected" or "unknown"))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.ValidationFailed, "'result' must be accepted, rejected or unknown.");
        }

        var visible = await _namespaces.GetByOwnerAsync(OwnerId, AllowedNamespaceIds, cancellationToken);
        if (visible.IsFailure)
        {
            return Problem(visible.Error);
        }

        var candidates = visible.Value.AsEnumerable();
        if (namespaceId is { } id)
        {
            candidates = candidates.Where(n => n.Id == id);
            if (!candidates.Any())
            {
                return Problem(StatusCodes.Status404NotFound, ErrorCodes.Namespace.NotFound, $"Namespace with ID '{id}' was not found.");
            }
        }

        if (provider is { } cloud)
        {
            candidates = candidates.Where(n => n.Provider == cloud);
        }

        return Ok(await _replay.ListAsync([.. candidates.Select(n => n.Id)], result, dlqMessageId, page ?? 1, pageSize ?? 25, cancellationToken));
    }
}
