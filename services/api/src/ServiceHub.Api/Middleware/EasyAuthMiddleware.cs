using Microsoft.Extensions.Logging;

namespace ServiceHub.Api.Middleware;

/// <summary>
/// Middleware for Azure App Service Easy Authentication (Built-in authentication).
/// Reads the X-MS-CLIENT-PRINCIPAL-ID header injected by Azure's authentication layer
/// and sets per-user OwnerId for tenant isolation.
/// 
/// This header is:
/// - Injected by Azure's reverse proxy AFTER successful Microsoft authentication
/// - STRIPPED from all inbound external requests (Postman, curl, etc cannot spoof it)
/// - Contains the user's Entra Object ID (globally unique, unforgeable)
/// 
/// Runs BEFORE ApiKeyAuthenticationMiddleware so that EasyAuth-authenticated
/// requests bypass the legacy SPA token path.
/// </summary>
public sealed class EasyAuthMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<EasyAuthMiddleware> _logger;
    private readonly bool _enabled;

    private const string EasyAuthHeaderName = "X-MS-CLIENT-PRINCIPAL-ID";

    /// <summary>
    /// Initializes a new instance of the <see cref="EasyAuthMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="configuration">The configuration.</param>
    public EasyAuthMiddleware(
        RequestDelegate next,
        ILogger<EasyAuthMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Easy Auth is always enabled when this middleware is registered.
        // In Development, Azure Easy Auth is OFF so this middleware sees no headers
        // and passes through without setting OwnerId (allowing legacy SPA token path).
        _enabled = true;
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        // Check for the Azure Easy Auth header
        if (_enabled && context.Request.Headers.TryGetValue(EasyAuthHeaderName, out var principalIdHeader))
        {
            var principalId = principalIdHeader.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(principalId))
            {
                // Construct OwnerId as "entra:{oid}" for consistency
                var ownerId = $"entra:{principalId}";
                context.Items["OwnerId"] = ownerId;
                context.Items["Authenticated"] = true;
                context.Items["AuthMethod"] = "EasyAuth";

                _logger.LogDebug(
                    "EasyAuth authentication successful for {Method} {Path} with OwnerId {OwnerId}",
                    context.Request.Method,
                    context.Request.Path,
                    principalId);

                // Continue to next middleware (skip ApiKeyAuthenticationMiddleware logic)
                await _next(context);
                return;
            }
        }

        // No Easy Auth header (either disabled or unauthenticated request from public internet)
        // In Development: no Easy Auth, continue to ApiKeyAuthenticationMiddleware (allows SPA token)
        // In Production: Azure infrastructure prevents unauthenticated requests from reaching here
        await _next(context);
    }
}
