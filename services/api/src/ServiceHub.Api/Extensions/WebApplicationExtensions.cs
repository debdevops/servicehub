using Scalar.AspNetCore;
using ServiceHub.Api.Configuration;
using ServiceHub.Api.Middleware;
using ServiceHub.Api.Security;

namespace ServiceHub.Api.Extensions;

/// <summary>
/// Extension methods for configuring WebApplication endpoints.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Maps all ServiceHub API endpoints.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The web application for chaining.</returns>
    public static WebApplication MapServiceHubEndpoints(this WebApplication app)
    {
        // Serve React SPA static files from wwwroot (production only)
        // In development, the React dev server runs separately on port 3000
        if (!app.Environment.IsDevelopment())
        {
            // Inject SPA token into index.html before serving static files
            var spaTokenProvider = app.Services.GetRequiredService<SpaTokenProvider>();
            if (spaTokenProvider.IsEnabled)
            {
                app.UseMiddleware<SpaTokenInjectionMiddleware>();
            }

            app.UseDefaultFiles();   // serves index.html for /
            app.UseStaticFiles();    // serves JS, CSS, images from wwwroot
        }

        // Map health check endpoints
        app.MapHealthCheckEndpoints();

        // Map controller endpoints
        app.MapControllers();

        // SPA fallback: for any route not matched by controllers or static files,
        // serve index.html so React Router handles client-side navigation.
        // This MUST come after MapControllers() so API routes take priority.
        if (!app.Environment.IsDevelopment())
        {
            app.MapFallbackToFile("index.html");
        }
        else
        {
            // In development, expose the OpenAPI document and Scalar UI
            app.MapOpenApi();                          // serves OpenAPI JSON at /openapi/v1.json
            app.MapScalarApiReference();               // serves Scalar UI at /scalar/v1
            app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
        }

        return app;
    }
}
