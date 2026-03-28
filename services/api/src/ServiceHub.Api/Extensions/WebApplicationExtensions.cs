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
            // Check if index.html exists before mapping fallback
            // (May not exist during testing or in non-hosted scenarios)
            var indexPath = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
            if (File.Exists(indexPath))
            {
                app.MapFallbackToFile("index.html");
            }
        }
        else
        {
            // In development, expose the OpenAPI document and Scalar UI
            // (Skip if wwwroot doesn't exist — indicates test/non-SPA environment)
            if (Directory.Exists(app.Environment.WebRootPath ?? "wwwroot"))
            {
                app.MapOpenApi();                          // serves OpenAPI JSON at /openapi/v1.json
                app.MapScalarApiReference();               // serves Scalar UI at /scalar/v1
                app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
            }

            // Internal endpoint for Vite dev server to fetch SPA tokens.
            // The Vite transformIndexHtml plugin calls this server-side and injects
            // the token into the HTML <meta> tag before serving to the browser.
            var spaTokenProvider = app.Services.GetRequiredService<SpaTokenProvider>();
            if (spaTokenProvider.IsEnabled)
            {
                app.MapGet("/internal/spa-token", () => Results.Text(spaTokenProvider.GenerateToken()))
                   .ExcludeFromDescription();
            }
        }

        return app;
    }
}
