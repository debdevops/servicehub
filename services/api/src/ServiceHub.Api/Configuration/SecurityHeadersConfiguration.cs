namespace ServiceHub.Api.Configuration;

/// <summary>
/// Configuration for HTTP security headers.
/// Centralizes all security header definitions for environment-specific customization.
/// </summary>
public static class SecurityHeadersConfiguration
{
    /// <summary>
    /// Gets security headers configuration from application settings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddSecurityHeadersConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<SecurityHeadersOptions>(configuration.GetSection("SecurityHeaders"));
        return services;
    }
}

/// <summary>
/// Options for HTTP security headers.
/// </summary>
public class SecurityHeadersOptions
{
    /// <summary>
    /// Gets or sets the X-Content-Type-Options header value.
    /// Prevents MIME type sniffing attacks.
    /// </summary>
    public string ContentTypeOptions { get; set; } = "nosniff";

    /// <summary>
    /// Gets or sets the X-Frame-Options header value.
    /// Prevents clickjacking attacks.
    /// </summary>
    public string FrameOptions { get; set; } = "DENY";

    /// <summary>
    /// Gets or sets the Referrer-Policy header value.
    /// Controls referrer information in requests.
    /// </summary>
    public string ReferrerPolicy { get; set; } = "strict-origin-when-cross-origin";

    /// <summary>
    /// Gets or sets the X-XSS-Protection header value.
    /// Legacy XSS protection (deprecated but still useful for older browsers).
    /// </summary>
    public string XssProtection { get; set; } = "1; mode=block";

    /// <summary>
    /// Gets or sets the Content-Security-Policy header value for production.
    /// Restrictive policy for API endpoints.
    /// </summary>
    public string ContentSecurityPolicyProduction { get; set; } = "default-src 'none'; frame-ancestors 'none'";

    /// <summary>
    /// Gets or sets the Content-Security-Policy header value for development/staging.
    /// More permissive to allow Swagger UI.
    /// </summary>
    public string ContentSecurityPolicyDevelopment { get; set; } = 
        "default-src 'self'; " +
        "script-src 'self' 'unsafe-inline' 'unsafe-eval'; " +
        "style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; " +
        "font-src 'self'; " +
        "frame-ancestors 'none'";

    /// <summary>
    /// Gets or sets the Permissions-Policy header value.
    /// Disables unnecessary browser features.
    /// </summary>
    public string PermissionsPolicy { get; set; } = 
        "accelerometer=(), " +
        "camera=(), " +
        "geolocation=(), " +
        "gyroscope=(), " +
        "magnetometer=(), " +
        "microphone=(), " +
        "payment=(), " +
        "usb=()";

    /// <summary>
    /// Gets or sets the Strict-Transport-Security header value (production only).
    /// Enforces HTTPS connections for 1 year including subdomains.
    /// </summary>
    public string StrictTransportSecurity { get; set; } = "max-age=31536000; includeSubDomains";

    /// <summary>
    /// Gets or sets the X-API-Version header value.
    /// Indicates the API version for caching proxies.
    /// </summary>
    public string ApiVersion { get; set; } = "1.0";

    /// <summary>
    /// Gets or sets a value indicating whether security headers are enabled.
    /// Default is true.
    /// </summary>
    public bool Enabled { get; set; } = true;
}
