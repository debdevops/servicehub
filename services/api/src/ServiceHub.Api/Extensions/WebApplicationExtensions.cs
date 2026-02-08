using ServiceHub.Api.Configuration;

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
        // Map health check endpoints
        app.MapHealthCheckEndpoints();

        // Map controller endpoints
        app.MapControllers();

        // Root redirect to Swagger
        app.MapGet("/", () => Results.Redirect("/swagger"))
            .ExcludeFromDescription();

        return app;
    }
}
