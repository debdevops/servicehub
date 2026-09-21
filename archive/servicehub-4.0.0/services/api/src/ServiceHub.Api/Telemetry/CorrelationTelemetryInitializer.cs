using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.DataContracts;

namespace ServiceHub.Api.Telemetry;

/// <summary>
/// Enriches all telemetry items with the correlation ID from the HTTP context.
/// Maps the existing X-Correlation-Id header to Application Insights operation tracking.
/// </summary>
public sealed class CorrelationTelemetryInitializer : ITelemetryInitializer
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationTelemetryInitializer(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public void Initialize(ITelemetry telemetry)
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext == null)
        {
            return;
        }

        // Attach the correlation ID as a custom property
        if (httpContext.Items.TryGetValue("CorrelationId", out var correlationId)
            && correlationId is string id
            && !string.IsNullOrEmpty(id))
        {
            if (telemetry is ISupportProperties propTelemetry)
            {
                propTelemetry.Properties["CorrelationId"] = id;
            }
        }

        // Tag with the application version
        telemetry.Context.Component.Version = typeof(CorrelationTelemetryInitializer)
            .Assembly.GetName().Version?.ToString() ?? "unknown";
    }
}
