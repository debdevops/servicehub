using Microsoft.Extensions.Options;
using ServiceHub.Api.Configuration;

namespace ServiceHub.Api.Middleware;

/// <summary>
/// Middleware for adding security headers to all responses.
/// Implements defense-in-depth by setting appropriate HTTP security headers.
/// </summary>
public sealed class SecurityHeadersMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SecurityHeadersMiddleware> _logger;
    private readonly SecurityHeadersOptions _options;
    private readonly bool _isProduction;

    /// <summary>
    /// Initializes a new instance of the <see cref="SecurityHeadersMiddleware"/> class.
    /// </summary>
    /// <param name="next">The next middleware in the pipeline.</param>
    /// <param name="logger">The logger instance.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="options">The security headers options.</param>
    public SecurityHeadersMiddleware(
        RequestDelegate next,
        ILogger<SecurityHeadersMiddleware> logger,
        IHostEnvironment environment,
        IOptions<SecurityHeadersOptions> options)
    {
        _next = next;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? new SecurityHeadersOptions();
        _isProduction = environment.IsProduction();
    }

    /// <summary>
    /// Invokes the middleware.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    public async Task InvokeAsync(HttpContext context)
    {
        if (!_options.Enabled)
        {
            await _next(context);
            return;
        }

        // Add security headers before processing the request
        context.Response.OnStarting(() =>
        {
            var headers = context.Response.Headers;

            try
            {
                // Prevent MIME type sniffing
                headers.Append("X-Content-Type-Options", _options.ContentTypeOptions);

                // Prevent clickjacking
                headers.Append("X-Frame-Options", _options.FrameOptions);

                // Control referrer information
                headers.Append("Referrer-Policy", _options.ReferrerPolicy);

                // Prevent XSS attacks (legacy header, but still useful)
                headers.Append("X-XSS-Protection", _options.XssProtection);

                // Content Security Policy - restrictive for production, permissive for dev
                var csp = _isProduction
                    ? _options.ContentSecurityPolicyProduction
                    : _options.ContentSecurityPolicyDevelopment;
                headers.Append("Content-Security-Policy", csp);

                // Permissions Policy - disable unnecessary features
                headers.Append("Permissions-Policy", _options.PermissionsPolicy);

                // HSTS - only in production over HTTPS
                if (_isProduction && context.Request.IsHttps)
                {
                    headers.Append("Strict-Transport-Security", _options.StrictTransportSecurity);
                }

                // Indicate this is an API (helps with caching proxies)
                headers.Append("X-API-Version", _options.ApiVersion);

                // Remove potentially dangerous headers
                headers.Remove("Server");
                headers.Remove("X-Powered-By");
                headers.Remove("X-AspNet-Version");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error adding security headers to response");
            }

            return Task.CompletedTask;
        });

        await _next(context);
    }
}
