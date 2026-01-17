using Microsoft.Extensions.Options;
using ServiceHub.Api.Configuration;
using ServiceHub.Api.Middleware;

namespace ServiceHub.Api.Extensions;

/// <summary>
/// Extension methods for configuring the application pipeline.
/// </summary>
public static class ApplicationBuilderExtensions
{
    /// <summary>
    /// Configures the ServiceHub API middleware pipeline.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="environment">The host environment.</param>
    /// <returns>The application builder for chaining.</returns>
    public static IApplicationBuilder UseServiceHubApi(this IApplicationBuilder app, IHostEnvironment environment)
    {
        // Security headers (must be early to apply to all responses)
        app.UseMiddleware<SecurityHeadersMiddleware>();

        // Error handling (must be early to catch all exceptions)
        app.UseMiddleware<ErrorHandlingMiddleware>();

        // Correlation ID
        app.UseMiddleware<CorrelationIdMiddleware>();

        // Request logging (with redaction)
        app.UseMiddleware<RequestLoggingMiddleware>();

        // API Key authentication
        app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

        // Rate limiting (skip in development for easier testing)
        if (!environment.IsDevelopment())
        {
            // Get headers options from DI to pass to middleware
            var headersOptions = app.ApplicationServices.GetRequiredService<IOptions<HttpHeadersOptions>>().Value;
            app.UseMiddleware<RateLimitingMiddleware>(null, headersOptions);
        }

        // Response compression
        app.UseResponseCompression();

        // CORS
        app.UseCorsConfiguration(environment);

        // Swagger (development and staging)
        if (environment.IsDevelopment() || environment.IsStaging())
        {
            app.UseSwaggerConfiguration();
        }

        // Routing
        app.UseRouting();

        // Response caching
        app.UseResponseCaching();

        return app;
    }
}
