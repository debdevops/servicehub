using Microsoft.AspNetCore.Mvc;
using ServiceHub.Api.Authorization;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Api.Controllers.V1;

/// <summary>
/// Provides a unified bridge to multi-cloud messaging providers (AWS SQS/SNS, GCP Pub/Sub).
/// Endpoints in this controller are only functional when the corresponding cloud provider
/// feature flag (<c>CloudProviders:Aws:Enabled</c> / <c>CloudProviders:Gcp:Enabled</c>) is set.
/// </summary>
[Route("api/v1/cloud-bridge")]
[Tags("CloudBridge")]
public sealed class CloudBridgeController : ApiControllerBase
{
    private readonly IEnumerable<ICloudMessagingProvider> _providers;
    private readonly ILogger<CloudBridgeController> _logger;

    /// <summary>
    /// Initialises a new <see cref="CloudBridgeController"/>.
    /// </summary>
    public CloudBridgeController(
        IEnumerable<ICloudMessagingProvider> providers,
        ILogger<CloudBridgeController> logger)
    {
        _providers = providers;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // GET api/v1/cloud-bridge/provider-status
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the registration status of every supported cloud provider.
    /// </summary>
    /// <remarks>
    /// A provider is considered "available" when it has been registered in the DI container
    /// (i.e. its feature flag was <c>true</c> at startup). The Azure provider is always
    /// present and is not listed here.
    /// </remarks>
    /// <response code="200">Dictionary of provider name → available flag.</response>
    [HttpGet("provider-status")]
    [RequireScope(ApiKeyScopes.NamespacesRead)]
    [ProducesResponseType(typeof(Dictionary<string, bool>), StatusCodes.Status200OK)]
    public IActionResult GetProviderStatus()
    {
        var registered = _providers.Select(p => p.ProviderType).ToHashSet();

        var status = new Dictionary<string, bool>
        {
            [CloudProviderType.Aws.ToString()] = registered.Contains(CloudProviderType.Aws),
            [CloudProviderType.Gcp.ToString()] = registered.Contains(CloudProviderType.Gcp),
        };

        return Ok(status);
    }

    // -------------------------------------------------------------------------
    // GET api/v1/cloud-bridge/namespaces/{namespaceId}/entities?provider=Aws
    // -------------------------------------------------------------------------

    /// <summary>
    /// Lists all messaging entities (queues / topics / subscriptions) visible to the given
    /// namespace using the specified cloud provider.
    /// </summary>
    /// <param name="namespaceId">The ServiceHub namespace identifier.</param>
    /// <param name="provider">The cloud provider to query (<c>Aws</c> or <c>Gcp</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">List of cloud entities.</response>
    /// <response code="400">Invalid or missing provider parameter.</response>
    /// <response code="404">Provider not registered / not enabled.</response>
    /// <response code="502">Provider returned an error while listing entities.</response>
    [HttpGet("namespaces/{namespaceId:guid}/entities")]
    [RequireScope(ApiKeyScopes.NamespacesRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> ListEntities(
        [FromRoute] Guid namespaceId,
        [FromQuery] string provider,
        CancellationToken ct)
    {
        var resolved = ResolveProvider(provider, out var error);
        if (resolved is null) return error!;

        var result = await resolved.ListEntitiesAsync(namespaceId, ct).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            if (result.Error.Code.StartsWith("NotFound", StringComparison.OrdinalIgnoreCase))
                return NotFound(result.Error.Message);

            if (_logger.IsEnabled(LogLevel.Error))
                _logger.LogError("ListEntities failed for {Provider}/{Namespace}: {Error}",
                    provider, namespaceId, result.Error.Message);

            return StatusCode(StatusCodes.Status502BadGateway, result.Error.Message);
        }

        return Ok(result.Value);
    }

    // -------------------------------------------------------------------------
    // GET api/v1/cloud-bridge/namespaces/{namespaceId}/visibility/{queueName}?provider=Aws
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the visibility-window (SQS) or acknowledge-deadline (Pub/Sub) status
    /// for a named queue or subscription.
    /// </summary>
    /// <param name="namespaceId">The ServiceHub namespace identifier.</param>
    /// <param name="queueName">Queue name (SQS) or subscription name (Pub/Sub).</param>
    /// <param name="provider">The cloud provider to query (<c>Aws</c> or <c>Gcp</c>).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <response code="200">Provider-specific visibility / ack-deadline snapshot.</response>
    /// <response code="400">Invalid or missing provider parameter.</response>
    /// <response code="404">Provider not registered or entity not found.</response>
    /// <response code="502">Provider returned an error.</response>
    [HttpGet("namespaces/{namespaceId:guid}/visibility/{queueName}")]
    [RequireScope(ApiKeyScopes.NamespacesRead)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetVisibilityStatus(
        [FromRoute] Guid namespaceId,
        [FromRoute] string queueName,
        [FromQuery] string provider,
        CancellationToken ct)
    {
        var resolved = ResolveProvider(provider, out var error);
        if (resolved is null) return error!;

        if (resolved.ProviderType == CloudProviderType.Aws)
        {
            if (resolved.GetMessageReceiver() is not IVisibilityStatusProvider awsReceiver)
                return StatusCode(StatusCodes.Status502BadGateway, "AWS receiver unavailable.");

            var result = await awsReceiver.GetVisibilityWindowStatusAsync(namespaceId, queueName, ct)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError("AWS GetVisibilityWindowStatus failed for {Queue}: {Error}",
                        queueName, result.Error.Message);
                return StatusCode(StatusCodes.Status502BadGateway, result.Error.Message);
            }

            return Ok(result.Value);
        }

        if (resolved.ProviderType == CloudProviderType.Gcp)
        {
            if (resolved.GetMessageReceiver() is not IAckDeadlineStatusProvider gcpReceiver)
                return StatusCode(StatusCodes.Status502BadGateway, "GCP receiver unavailable.");

            var result = await gcpReceiver.GetAckDeadlineStatusAsync(namespaceId, queueName, ct)
                .ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                if (_logger.IsEnabled(LogLevel.Error))
                    _logger.LogError("GCP GetAckDeadlineStatus failed for {Subscription}: {Error}",
                        queueName, result.Error.Message);
                return StatusCode(StatusCodes.Status502BadGateway, result.Error.Message);
            }

            return Ok(result.Value);
        }

        return BadRequest($"Visibility status is not supported for provider '{provider}'.");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private ICloudMessagingProvider? ResolveProvider(string? provider, out IActionResult? error)
    {
        if (string.IsNullOrWhiteSpace(provider))
        {
            error = BadRequest("The 'provider' query parameter is required (e.g. ?provider=Aws).");
            return null;
        }

        if (!Enum.TryParse<CloudProviderType>(provider, ignoreCase: true, out var providerType))
        {
            error = BadRequest($"Unknown provider '{provider}'. Valid values: Aws, Gcp.");
            return null;
        }

        var resolved = _providers.FirstOrDefault(p => p.ProviderType == providerType);
        if (resolved is null)
        {
            error = StatusCode(StatusCodes.Status503ServiceUnavailable, new ProblemDetails
            {
                Status = StatusCodes.Status503ServiceUnavailable,
                Title = "Provider not enabled",
                Detail = $"The '{provider}' cloud provider is not enabled on this server. " +
                         $"Set 'CloudProviders:{provider}:Enabled' to 'true' in appsettings and restart.",
                Instance = HttpContext.Request.Path
            });
            return null;
        }

        error = null;
        return resolved;
    }
}
