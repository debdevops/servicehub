using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Home as a ranked attention queue (roadmap W2.2) — the three failure signatures across an
/// owner's fleet most worth a human's attention right now.
/// </summary>
[Route(ApiRoutes.AttentionQueue.Base)]
[Tags("Attention Queue")]
public sealed class AttentionQueueController : ApiControllerBase
{
    private readonly IAttentionQueueService _attentionQueueService;

    /// <summary>Initializes a new instance of the <see cref="AttentionQueueController"/> class.</summary>
    public AttentionQueueController(IAttentionQueueService attentionQueueService)
    {
        _attentionQueueService = attentionQueueService ?? throw new ArgumentNullException(nameof(attentionQueueService));
    }

    /// <summary>Gets the ranked, capped attention queue for the caller across every namespace they own.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(AttentionQueueResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<AttentionQueueResponse>> GetAttentionQueueAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await _attentionQueueService.GetAttentionQueueAsync(OwnerId, cancellationToken);
        return ToActionResult(result);
    }
}
