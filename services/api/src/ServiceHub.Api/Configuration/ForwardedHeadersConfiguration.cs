namespace ServiceHub.Api.Configuration;

/// <summary>
/// Configuration for X-Forwarded-* header handling in reverse proxy scenarios.
/// By default, forwarded headers are disabled for security (untrusted headers are ignored).
/// Enable only when deployed behind a known reverse proxy, and explicitly specify
/// the proxy's IP address or CIDR range to trust.
/// </summary>
public class ForwardedHeadersConfiguration
{
    /// <summary>
    /// Whether to trust X-Forwarded-* headers from reverse proxies.
    /// Default: false (safe, but requires configuration if behind a proxy).
    /// Set to true only if deployed behind a trusted reverse proxy.
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Individual IP addresses of trusted proxies.
    /// Example: ["10.0.0.1", "10.0.0.2"]
    /// </summary>
    public List<string> KnownProxies { get; set; } = new();

    /// <summary>
    /// CIDR ranges of trusted proxies.
    /// Example: ["10.0.0.0/8", "192.168.0.0/16"]
    /// </summary>
    public List<string> KnownNetworks { get; set; } = new();

    /// <summary>
    /// Environment detection for automatic Azure App Service configuration.
    /// Set to true to automatically trust Azure App Service (WEBSITE_AUTH_ENABLED=true).
    /// </summary>
    public bool AutoDetectAzureAppService { get; set; } = true;

    /// <summary>
    /// Whether to use X-Forwarded-For header (client IP).
    /// </summary>
    public bool UseXForwardedFor { get; set; } = true;

    /// <summary>
    /// Whether to use X-Forwarded-Proto header (scheme: http/https).
    /// </summary>
    public bool UseXForwardedProto { get; set; } = true;
}
