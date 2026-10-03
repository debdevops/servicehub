using Microsoft.Extensions.Options;
using ServiceHub.Api.Configuration;
using ServiceHub.Api.Middleware;
using Swashbuckle.AspNetCore.SwaggerUI;

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

        // CORS must run BEFORE authentication so that OPTIONS preflight requests
        // are handled here (with 204 + CORS headers) and never reach the auth
        // middleware. Moving it after auth causes CORS preflights to get 401.
        app.UseCorsConfiguration(environment);

        // Azure Easy Auth middleware (reads X-MS-CLIENT-PRINCIPAL-ID from Azure's reverse proxy)
        // Must run BEFORE ApiKeyAuthenticationMiddleware to set per-user OwnerId for tenant isolation.
        // In Development (Easy Auth disabled): passes through, allows legacy SPA token path.
        // In Production (Easy Auth enabled): sets OwnerId from Entra Object ID.
        app.UseMiddleware<EasyAuthMiddleware>();

        // OIDC Bearer token authentication — the provider-neutral counterpart to EasyAuth above,
        // for any standards-compliant identity provider (Entra ID, Okta, Auth0, ...) on any host
        // (AWS, GCP, Docker Compose, bare Kestrel). Disabled by default (Security:Oidc:Enabled).
        // Must also run BEFORE ApiKeyAuthenticationMiddleware, same reason as EasyAuth.
        app.UseMiddleware<OidcBearerAuthenticationMiddleware>();

        // API Key authentication (legacy path for dev or external API clients)
        //
        // DELIBERATE ORDER — do not move RateLimitingMiddleware before this. RateLimitingMiddleware's
        // GetClientIdentifier prefers the authenticated OwnerId over the raw IP; moving it earlier
        // would degrade every authenticated request to IP-based keying, and since ForwardedHeaders
        // is configured with KnownProxies cleared, all requests behind a reverse proxy would then
        // collapse into one shared bucket. That trades an auth hole for a tenant-fairness
        // regression. API key brute force is throttled separately, inside this middleware, via
        // AuthFailureThrottle — which runs regardless of pipeline position because it keys on IP
        // and acts before any credential is evaluated.
        app.UseMiddleware<ApiKeyAuthenticationMiddleware>();

        // Rate limiting (skip in development for easier testing)
        if (!environment.IsDevelopment())
        {
            // Get options from DI — passing null as an arg breaks ActivatorUtilities in .NET 10
            var rateLimitOptions = app.ApplicationServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;
            var headersOptions = app.ApplicationServices.GetRequiredService<IOptions<HttpHeadersOptions>>().Value;
            app.UseMiddleware<RateLimitingMiddleware>(rateLimitOptions, headersOptions);
        }

        // Response compression
        app.UseResponseCompression();

        // Swagger UI (development only) — must be before UseRouting()
        if (environment.IsDevelopment())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "ServiceHub API v1");
                c.RoutePrefix = "swagger"; // serves at /swagger/index.html
            });
        }

        // OpenAPI document and Scalar UI are mapped in WebApplicationExtensions.cs (requires WebApplication, not IApplicationBuilder)

        // Routing
        app.UseRouting();

        // Response caching
        app.UseResponseCaching();

        return app;
    }
}
