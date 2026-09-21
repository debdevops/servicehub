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
    private readonly bool _isDevelopment;

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
        _isDevelopment = environment.IsDevelopment();
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
                // Assignment, not Append. Append adds a second value to a header that is already
                // present, so anything set upstream (a reverse proxy, another middleware, a
                // static-file handler) produced a duplicated header such as
                // "X-Frame-Options: DENY, DENY". Browsers treat a conflicting duplicate of some
                // of these as invalid and may ignore the header entirely — which would silently
                // disable the protection this middleware exists to add. Assignment makes this
                // middleware authoritative, which is the intent.

                // Prevent MIME type sniffing
                headers["X-Content-Type-Options"] = _options.ContentTypeOptions;

                // Prevent clickjacking
                headers["X-Frame-Options"] = _options.FrameOptions;

                // Control referrer information
                headers["Referrer-Policy"] = _options.ReferrerPolicy;

                // Prevent XSS attacks (legacy header, but still useful)
                headers["X-XSS-Protection"] = _options.XssProtection;

                // Content Security Policy - permissive only in Development; every other
                // environment (Production, Staging, any custom name) gets the
                // restrictive policy. Deliberately keyed on IsDevelopment(), not IsProduction() —
                // the inverse would silently apply the permissive dev policy to any non-Production
                // environment name, which is the defect this predicate exists to prevent.
                var csp = _isDevelopment
                    ? _options.ContentSecurityPolicyDevelopment
                    : _options.ContentSecurityPolicyProduction;
                headers["Content-Security-Policy"] = csp;

                // Permissions Policy - disable unnecessary features
                headers["Permissions-Policy"] = _options.PermissionsPolicy;

                // HSTS - only in production over HTTPS
                if (_isProduction && context.Request.IsHttps)
                {
                    headers["Strict-Transport-Security"] = _options.StrictTransportSecurity;
                }

                // Indicate this is an API (helps with caching proxies)
                headers["X-API-Version"] = _options.ApiVersion;

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
