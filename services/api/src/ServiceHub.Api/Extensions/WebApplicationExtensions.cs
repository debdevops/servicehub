using Scalar.AspNetCore;
using ServiceHub.Api.Configuration;
using ServiceHub.Api.Middleware;
using ServiceHub.Api.Security;
using Swashbuckle.AspNetCore.SwaggerUI;

namespace ServiceHub.Api.Extensions;

/// <summary>
/// Extension methods for configuring WebApplication endpoints.
/// </summary>
public static class WebApplicationExtensions
{
    /// <summary>
    /// Validates that the Origin or Referer header matches the request host.
    /// Performs exact host matching to prevent bypasses like "example.com.evil.com".
    /// </summary>
    private static bool IsValidOrigin(string headerValue, string expectedHost)
    {
        if (string.IsNullOrEmpty(headerValue))
            return false;

        try
        {
            if (Uri.TryCreate(headerValue, UriKind.Absolute, out var uri))
            {
                // Compare the host portion exactly (IdnHost handles internationalized domain names)
                return string.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase);
            }
        }
        catch
        {
            // Invalid URI format — reject
            return false;
        }

        return false;
    }

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
            // In development, expose the OpenAPI document and API documentation UIs
            // (Skip if wwwroot doesn't exist — indicates test/non-SPA environment)
            if (Directory.Exists(app.Environment.WebRootPath ?? "wwwroot"))
            {
                app.MapOpenApi();                          // serves OpenAPI JSON at /openapi/v1.json
                app.MapScalarApiReference();               // serves Scalar UI at /scalar/v1
                // Swagger UI is mapped via middleware in ApplicationBuilderExtensions
                app.MapGet("/", () => Results.Redirect("/scalar/v1")).ExcludeFromDescription();
            }
        }

        {
            // Expose the SPA token refresh endpoint in ALL environments.
            // The browser SPA calls this when its embedded token expires (30-min lifetime)
            // or when Azure App Service load-balancer routes the request to a different
            // instance that has a different ephemeral key. The endpoint is intentionally
            // NOT under /api/ so the ApiKeyAuthenticationMiddleware bypass rule lets it
            // through without any credential, which is necessary to bootstrap auth.
            // Security hardening: same-origin enforcement + tight rate limit.
            // Use GetService (returns null if not found) instead of GetRequiredService
            var spaTokenProvider = app.Services.GetService<SpaTokenProvider>();
            if (spaTokenProvider?.IsEnabled == true)
            {
                // Per-IP token request counter for rate limiting this unauthenticated endpoint.
                var tokenRateLimit = new System.Collections.Concurrent.ConcurrentDictionary<string, (int Count, DateTime WindowStart)>();

                // Background cleanup: prune entries older than 5 minutes to prevent unbounded growth.
                var cts = new System.Threading.CancellationTokenSource();
                _ = Task.Run(async () =>
                {
                    while (!cts.Token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(2), cts.Token).ConfigureAwait(false);
                        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(5);
                        foreach (var kvp in tokenRateLimit)
                        {
                            if (kvp.Value.WindowStart < cutoff)
                                tokenRateLimit.TryRemove(kvp.Key, out _);
                        }
                    }
                }, cts.Token);

                // Register cleanup on app shutdown
                var lifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                lifetime.ApplicationStopping.Register(() => cts.Cancel());

                app.MapGet("/internal/spa-token", (HttpContext ctx) =>
                {
                    // Same-origin enforcement: only serve a token to requests that originated
                    // from our own HTML page (Origin or Referer must match the host).
                    // This blocks bare curl/wget calls that don't send an Origin header.
                    var host = ctx.Request.Host.Host;
                    var origin = ctx.Request.Headers.Origin.FirstOrDefault() ?? string.Empty;
                    var referer = ctx.Request.Headers.Referer.FirstOrDefault() ?? string.Empty;

                    // In production the request MUST carry an Origin or Referer from our host.
                    if (!app.Environment.IsDevelopment())
                    {
                        var hasValidOrigin = IsValidOrigin(origin, host);
                        var hasValidReferer = IsValidOrigin(referer, host);
                        if (!hasValidOrigin && !hasValidReferer)
                        {
                            return Results.StatusCode(403);
                        }
                    }

                    // Tight per-IP rate limit: 10 requests per minute (covers tab refreshes
                    // + load-balancer re-routing). Uses AddOrUpdate to avoid TOCTOU race condition.
                    var ip = ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                    var now = DateTime.UtcNow;
                    var entry = tokenRateLimit.AddOrUpdate(
                        ip,
                        _ => (1, now),
                        (_, existing) =>
                        {
                            if (now - existing.WindowStart > TimeSpan.FromMinutes(1))
                                return (1, now);
                            return (existing.Count + 1, existing.WindowStart);
                        });

                    if (entry.Count > 10)
                    {
                        ctx.Response.Headers.RetryAfter = "60";
                        return Results.StatusCode(429);
                    }

                    return Results.Text(spaTokenProvider.GenerateToken());
                }).ExcludeFromDescription();
            }
        }

        // Sitemap for search engine indexing
        app.MapGet("/sitemap.xml", (IConfiguration config) =>
        {
            var baseUrl = (config.GetValue<string>("SiteUrl") ?? string.Empty).TrimEnd('/');

            var sitemap = $"""
                <?xml version="1.0" encoding="UTF-8"?>
                <urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
                  <url>
                    <loc>{baseUrl}/</loc>
                    <changefreq>monthly</changefreq>
                    <priority>1.0</priority>
                  </url>
                  <url>
                    <loc>{baseUrl}/connect</loc>
                    <changefreq>monthly</changefreq>
                    <priority>0.9</priority>
                  </url>
                  <url>
                    <loc>{baseUrl}/security</loc>
                    <changefreq>monthly</changefreq>
                    <priority>0.8</priority>
                  </url>
                  <url>
                    <loc>{baseUrl}/help</loc>
                    <changefreq>monthly</changefreq>
                    <priority>0.7</priority>
                  </url>
                </urlset>
                """;

            return Results.Content(sitemap, "application/xml");
        })
        .ExcludeFromDescription()
        .AllowAnonymous();

        return app;
    }
}
