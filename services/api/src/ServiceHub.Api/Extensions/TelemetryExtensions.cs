using Microsoft.ApplicationInsights.Extensibility;
using ServiceHub.Api.Telemetry;

namespace ServiceHub.Api.Extensions;

/// <summary>
/// Extension methods for configuring Application Insights telemetry.
/// </summary>
public static class TelemetryExtensions
{
    /// <summary>
    /// Adds Application Insights telemetry with cost-effective configuration.
    /// Uses adaptive sampling, filters health checks, and enriches with correlation IDs.
    /// </summary>
    public static IServiceCollection AddApplicationInsightsTelemetryConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Only enable if a connection string is configured
        var connectionString = configuration["ApplicationInsights:ConnectionString"];
        if (string.IsNullOrEmpty(connectionString))
        {
            return services;
        }

        services.AddApplicationInsightsTelemetry(options =>
        {
            options.ConnectionString = connectionString;

            // Disable heartbeat to reduce telemetry volume
            options.EnableHeartbeat = false;

            // Disable performance counters in development to save cost
            options.EnablePerformanceCounterCollectionModule = !environment.IsDevelopment();
        });

        // Add custom telemetry initializer for correlation IDs
        services.AddSingleton<ITelemetryInitializer, CorrelationTelemetryInitializer>();

        // Strip sensitive data (connection strings, API keys, tokens) before sending to Application Insights
        // Must be registered BEFORE HealthCheckTelemetryFilter so it runs first in the pipeline
        services.AddApplicationInsightsTelemetryProcessor<SensitiveDataTelemetryProcessor>();

        // Filter out health check and internal endpoint telemetry to reduce cost
        services.AddApplicationInsightsTelemetryProcessor<HealthCheckTelemetryFilter>();

        return services;
    }
}
