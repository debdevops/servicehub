using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using System.Text.Json;

namespace ServiceHub.Api.Configuration;

/// <summary>
/// Configuration for health check endpoints.
/// </summary>
public static class HealthCheckConfiguration
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    /// <summary>
    /// Adds health check services to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddHealthCheckConfiguration(this IServiceCollection services)
    {
        services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy("API is running"), tags: ["live"]);

        return services;
    }

    /// <summary>
    /// Maps health check endpoints.
    /// </summary>
    /// <param name="app">The endpoint route builder.</param>
    /// <returns>The endpoint route builder for chaining.</returns>
    public static IEndpointRouteBuilder MapHealthCheckEndpoints(this IEndpointRouteBuilder app)
    {
        // Liveness probe - checks if the application is running
        app.MapHealthChecks("/health/live", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("live"),
            ResponseWriter = WriteResponse
        });

        // Readiness probe - checks if the application can handle requests
        app.MapHealthChecks("/health/ready", new HealthCheckOptions
        {
            Predicate = check => check.Tags.Contains("ready"),
            ResponseWriter = WriteResponse
        });

        // Full health check - all registered checks
        app.MapHealthChecks("/health", new HealthCheckOptions
        {
            ResponseWriter = WriteResponse
        });

        return app;
    }

    private static async Task WriteResponse(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";

        var response = new HealthCheckResponse
        {
            Status = report.Status.ToString(),
            TotalDuration = report.TotalDuration.TotalMilliseconds,
            Entries = report.Entries.ToDictionary(
                entry => entry.Key,
                entry => new HealthCheckEntryResponse
                {
                    Status = entry.Value.Status.ToString(),
                    Description = entry.Value.Description,
                    Duration = entry.Value.Duration.TotalMilliseconds,
                    Data = entry.Value.Data.Count > 0 ? entry.Value.Data : null,
                    Exception = entry.Value.Exception?.Message
                }
            )
        };

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }

    private sealed class HealthCheckResponse
    {
        public string Status { get; set; } = string.Empty;
        public double TotalDuration { get; set; }
        public Dictionary<string, HealthCheckEntryResponse> Entries { get; set; } = [];
    }

    private sealed class HealthCheckEntryResponse
    {
        public string Status { get; set; } = string.Empty;
        public string? Description { get; set; }
        public double Duration { get; set; }
        public IReadOnlyDictionary<string, object>? Data { get; set; }
        public string? Exception { get; set; }
    }
}
