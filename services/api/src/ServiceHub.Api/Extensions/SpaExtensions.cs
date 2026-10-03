namespace ServiceHub.Api.Extensions;

/// <summary>Serves the built SPA from <c>wwwroot</c>.</summary>
/// <remarks>
/// One application, at the origin root, with no prefix. There is no second app to mount and no cutover.
/// </remarks>
public static class SpaExtensions
{
    /// <summary>Serves static files and falls back to <c>index.html</c> for client-side routes.</summary>
    public static WebApplication MapServiceHubSpa(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.UseDefaultFiles();
        app.UseStaticFiles();

        // Anything that is not an API route, a health probe or a real file is a client-side route.
        // An unknown /api/** path must still 404 as an API, not silently return HTML.
        app.MapFallback(async context =>
        {
            var path = context.Request.Path;
            if (path.StartsWithSegments("/api") || path.StartsWithSegments("/health"))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var index = Path.Combine(app.Environment.WebRootPath ?? "wwwroot", "index.html");
            if (!File.Exists(index))
            {
                // Before the SPA is built — say so plainly rather than returning an empty 404 that
                // looks like a routing bug.
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                await context.Response.WriteAsync(
                    "The ServiceHub web application has not been built. Run: npm run build -w apps/servicehub")
                    .ConfigureAwait(false);
                return;
            }

            context.Response.ContentType = "text/html";
            await context.Response.SendFileAsync(index).ConfigureAwait(false);
        });

        return app;
    }
}
