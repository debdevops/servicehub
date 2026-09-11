using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Shared.Constants;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Cross-cloud dead-letter overview — the provider-grouped triage dashboard answering "which
/// cloud, which reasons, which namespaces, and is it getting better or worse?"
/// </summary>
[Route(ApiRoutes.Dlq.Base)]
[Tags("DLQ Intelligence")]
public sealed class DlqOverviewController : ApiControllerBase
{
    private readonly IDlqOverviewService _overviewService;

    /// <summary>Initializes a new instance of the <see cref="DlqOverviewController"/> class.</summary>
    public DlqOverviewController(IDlqOverviewService overviewService)
    {
        _overviewService = overviewService ?? throw new ArgumentNullException(nameof(overviewService));
    }

    /// <summary>
    /// Gets a provider-grouped DLQ overview across all namespaces owned by the caller.
    /// </summary>
    /// <param name="days">The trend/comparison window in days (default 7, clamped 1–90).</param>
    /// <param name="cloud">Optional cloud provider filter.</param>
    /// <param name="environment">Optional environment filter.</param>
    /// <param name="namespaceId">Optional single-namespace filter.</param>
    /// <param name="reason">Optional failure-category filter.</param>
    /// <param name="entityName">Optional entity-name substring filter.</param>
    /// <param name="status">Optional message-status filter.</param>
    /// <param name="replaySafety">Optional replay-safety filter (Safe, RequiresReview, Unsafe).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The cross-cloud DLQ overview snapshot.</returns>
    [HttpGet("overview")]
    [RequireScope(ApiKeyScopes.DlqRead)]
    [ProducesResponseType(typeof(DlqOverview), StatusCodes.Status200OK)]
    public async Task<ActionResult<DlqOverview>> GetOverviewAsync(
        [FromQuery] int days = 7,
        [FromQuery] CloudProviderType? cloud = null,
        [FromQuery] EnvironmentType? environment = null,
        [FromQuery] Guid? namespaceId = null,
        [FromQuery] FailureCategory? reason = null,
        [FromQuery] string? entityName = null,
        [FromQuery] DlqMessageStatus? status = null,
        [FromQuery] string? replaySafety = null,
        CancellationToken cancellationToken = default)
    {
        var filter = new DlqOverviewFilter(days, cloud, environment, namespaceId, reason, entityName, status, replaySafety);
        var result = await _overviewService.GetOverviewAsync(OwnerId, filter, cancellationToken);
        return ToActionResult(result);
    }
}
