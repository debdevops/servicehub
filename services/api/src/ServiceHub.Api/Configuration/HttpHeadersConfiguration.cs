namespace ServiceHub.Api.Configuration;

/// <summary>
/// Configuration for HTTP header names used throughout the application.
/// Centralizes all header definitions to prevent hardcoding and enable environment-specific customization.
/// </summary>
public static class HttpHeadersConfiguration
{
    /// <summary>
    /// Gets HTTP headers configuration from application settings.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHttpHeadersConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HttpHeadersOptions>(configuration.GetSection("HttpHeaders"));
        return services;
    }
}

/// <summary>
/// Options for HTTP header names.
/// </summary>
public class HttpHeadersOptions
{
    /// <summary>
    /// Gets or sets the correlation ID header name.
    /// </summary>
    public string CorrelationId { get; set; } = "X-Correlation-Id";

    /// <summary>
    /// Gets or sets the rate limit maximum requests header name.
    /// </summary>
    public string RateLimitLimit { get; set; } = "X-RateLimit-Limit";

    /// <summary>
    /// Gets or sets the rate limit remaining requests header name.
    /// </summary>
    public string RateLimitRemaining { get; set; } = "X-RateLimit-Remaining";

    /// <summary>
    /// Gets or sets the rate limit reset time header name.
    /// </summary>
    public string RateLimitReset { get; set; } = "X-RateLimit-Reset";

    /// <summary>
    /// Gets or sets the total count header name (for pagination).
    /// </summary>
    public string TotalCount { get; set; } = "X-Total-Count";

    /// <summary>
    /// Gets or sets the page number header name.
    /// </summary>
    public string PageNumber { get; set; } = "X-Page-Number";

    /// <summary>
    /// Gets or sets the page size header name.
    /// </summary>
    public string PageSize { get; set; } = "X-Page-Size";

    /// <summary>
    /// Gets all exposed headers as an array for CORS configuration.
    /// </summary>
    /// <returns>Array of header names to expose to CORS clients.</returns>
    public string[] GetExposedHeaders() => new[]
    {
        CorrelationId,
        RateLimitLimit,
        RateLimitRemaining,
        RateLimitReset,
        TotalCount,
        PageNumber,
        PageSize
    };
}
