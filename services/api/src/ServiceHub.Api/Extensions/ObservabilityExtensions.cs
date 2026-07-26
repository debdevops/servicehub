using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ServiceHub.Api.Extensions;

/// <summary>
/// Vendor-neutral observability foundation built on OpenTelemetry (distributed traces and
/// runtime/HTTP metrics), exported over OTLP.
/// <para>
/// This is intentionally opt-in and <b>inert by default</b>: OpenTelemetry is only wired up
/// when <c>OpenTelemetry:Enabled</c> is <c>true</c> or an OTLP endpoint is configured
/// (via <c>OpenTelemetry:Otlp:Endpoint</c> or the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>
/// environment variable). When neither is present the method is a no-op, so existing
/// deployments — including the Azure App Insights path — are completely unaffected. This gives
/// AWS/GCP/on-prem operators a first-class, portable telemetry option without imposing a
/// runtime cost on anyone who has not asked for it.
/// </para>
/// </summary>
public static class ObservabilityExtensions
{
    /// <summary>The OpenTelemetry service name reported for all signals.</summary>
    public const string ServiceName = "ServiceHub";

    private const string EnabledKey = "OpenTelemetry:Enabled";
    private const string OtlpEndpointConfigKey = "OpenTelemetry:Otlp:Endpoint";
    private const string OtlpEndpointEnvKey = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>
    /// Adds OpenTelemetry tracing and metrics when explicitly enabled; otherwise does nothing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The application configuration.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddOpenTelemetryObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (!IsEnabled(configuration))
        {
            return services;
        }

        var otlpEndpoint = ResolveOtlpEndpoint(configuration);
        var serviceVersion = typeof(ObservabilityExtensions).Assembly.GetName().Version?.ToString() ?? "unknown";

        services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(
                serviceName: ServiceName,
                serviceVersion: serviceVersion))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        // Health/liveness probes are noisy and low-value as traces.
                        options.Filter = context =>
                            !context.Request.Path.StartsWithSegments("/health");
                    })
                    .AddHttpClientInstrumentation();

                if (otlpEndpoint is not null)
                {
                    tracing.AddOtlpExporter(o => o.Endpoint = otlpEndpoint);
                }
            })
            .WithMetrics(metrics =>
            {
                metrics
                    .AddMeter(Telemetry.ServiceHubMetrics.MeterName)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation();

                if (otlpEndpoint is not null)
                {
                    metrics.AddOtlpExporter(o => o.Endpoint = otlpEndpoint);
                }
            });

        return services;
    }

    private static bool IsEnabled(IConfiguration configuration)
        => configuration.GetValue(EnabledKey, false)
           || ResolveOtlpEndpoint(configuration) is not null;

    private static Uri? ResolveOtlpEndpoint(IConfiguration configuration)
    {
        var raw = configuration[OtlpEndpointConfigKey]
                  ?? Environment.GetEnvironmentVariable(OtlpEndpointEnvKey);

        return Uri.TryCreate(raw, UriKind.Absolute, out var uri) ? uri : null;
    }
}
