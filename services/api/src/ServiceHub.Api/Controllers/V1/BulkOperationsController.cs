using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Security;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Bulk replay (unit 3.2): <c>preview</c> → <c>start</c> → progress → <c>cancel</c>. There is no way to execute a
/// selection that was not previewed: start takes only a stored preview's id.
/// </summary>
[Route("api/v1/bulk-operations")]
public sealed class BulkOperationsController : ApiControllerBase
{
    private readonly IBulkOperationService _bulk;

    /// <summary>Creates the controller.</summary>
    public BulkOperationsController(IBulkOperationService bulk) => _bulk = bulk ?? throw new ArgumentNullException(nameof(bulk));

    /// <summary>What replaying these dead letters would do. Sends nothing.</summary>
    [HttpPost("preview")]
    [ProducesResponseType(typeof(BulkPreview), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Preview([FromBody] PreviewRequest request, CancellationToken cancellationToken)
    {
        var result = await _bulk.PreviewAsync(OwnerId, AllowedNamespaceIds, Actor, request?.DlqMessageIds ?? [], cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Ok(result.Value);
    }

    /// <summary>Starts the run from a stored preview.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(BulkProgress), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Start([FromBody] StartRequest request, CancellationToken cancellationToken)
    {
        if (!IntentHeaders.Declares(Request, IntentHeaders.BulkReplay))
        {
            return Problem(StatusCodes.Status400BadRequest, ErrorCodes.IntentRequired, IntentHeaders.MissingDetail("replay these messages", IntentHeaders.BulkReplay));
        }

        var result = await _bulk.StartAsync(OwnerId, request.PreviewId, request.SampleOnly, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Accepted(result.Value);
    }

    /// <summary>Where a bulk replay is.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(BulkProgress), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bulk.GetAsync(OwnerId, id, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Ok(result.Value);
    }

    /// <summary>Asks a running bulk replay to stop before its next message.</summary>
    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(BulkProgress), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken cancellationToken)
    {
        var result = await _bulk.CancelAsync(OwnerId, id, cancellationToken);
        return result.IsFailure ? Problem(result.Error) : Ok(result.Value);
    }

    /// <summary>The dead letters to preview.</summary>
    public sealed record PreviewRequest(IReadOnlyList<long> DlqMessageIds);

    /// <summary>The preview to start.</summary>
    public sealed record StartRequest(Guid PreviewId, bool SampleOnly = false);
}
