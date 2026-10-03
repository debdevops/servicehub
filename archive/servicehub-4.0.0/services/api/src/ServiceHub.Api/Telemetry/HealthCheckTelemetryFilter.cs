using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;

namespace ServiceHub.Api.Telemetry;

/// <summary>
/// Filters out health check and internal endpoint telemetry to reduce cost.
/// These endpoints are called frequently by load balancers and monitoring
/// and would generate significant unnecessary telemetry volume.
/// </summary>
public sealed class HealthCheckTelemetryFilter : ITelemetryProcessor
{
    private readonly ITelemetryProcessor _next;

    private static readonly string[] ExcludedPaths =
    [
        "/health",
        "/healthz",
        "/ready",
        "/internal/spa-token",
        "/openapi/",
        "/scalar/"
    ];

    public HealthCheckTelemetryFilter(ITelemetryProcessor next)
    {
        _next = next;
    }

    public void Process(ITelemetry item)
    {
        if (item is RequestTelemetry request)
        {
            var url = request.Url?.AbsolutePath;
            if (url != null && IsExcludedPath(url))
            {
                return; // Drop the telemetry item
            }
        }

        _next.Process(item);
    }

    private static bool IsExcludedPath(string path)
    {
        foreach (var excluded in ExcludedPaths)
        {
            if (path.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}
