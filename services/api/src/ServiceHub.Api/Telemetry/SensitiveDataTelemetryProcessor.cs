using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using ServiceHub.Infrastructure.Security;
using System.Text.RegularExpressions;
using System.Web;

namespace ServiceHub.Api.Telemetry;

/// <summary>
/// Ensures no sensitive data (connection strings, API keys, tokens) is sent to Application Insights.
/// Works in conjunction with LogRedactor which handles structured logging. This processor handles
/// the telemetry pipeline.
///
/// Runs on every telemetry item before it is sent to Azure Application Insights:
/// - RequestTelemetry:    Redacts sensitive query string parameters (key, token, secret, password, etc.)
/// - TraceTelemetry:      Runs message content through LogRedactor to strip connection string fragments
/// - ExceptionTelemetry:  Runs exception messages through LogRedactor to strip secrets
/// - DependencyTelemetry: Strips the Data field if it contains any connection string key material
/// </summary>
public sealed class SensitiveDataTelemetryProcessor : ITelemetryProcessor
{
    private readonly ITelemetryProcessor _next;

    private static readonly HashSet<string> SensitiveQueryParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "key",
        "token",
        "secret",
        "connectionstring",
        "password",
        "apikey",
        "api_key",
    };

    // Custom property keys that must never reach Application Insights
    // regardless of how they are set (e.g. by an AI analyzer or controller).
    private static readonly HashSet<string> SensitivePropertyKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "messageBody",
        "message_body",
        "body",
        "connectionString",
        "connection_string",
        "correlationId",   // Service Bus message-level correlation IDs (not infra tracing IDs)
        "userInput",
        "payload",
    };

    private static readonly string[] SensitiveDependencyPatterns =
    [
        "SharedAccessKey",
        "AccountKey",
        "SharedAccessSignature",
    ];

    public SensitiveDataTelemetryProcessor(ITelemetryProcessor next)
    {
        _next = next;
    }

    public void Process(ITelemetry item)
    {
        switch (item)
        {
            case RequestTelemetry request:
                RedactRequestTelemetry(request);
                break;

            case TraceTelemetry trace:
                trace.Message = LogRedactor.Redact(trace.Message);
                RedactProperties(trace.Properties);
                break;

            case ExceptionTelemetry exception:
                RedactExceptionTelemetry(exception);
                break;

            case DependencyTelemetry dependency:
                RedactDependencyTelemetry(dependency);
                break;

            case EventTelemetry evt:
                // Custom events must never carry message bodies or connection data
                RedactProperties(evt.Properties);
                break;
        }

        // Always pass the item through — never drop telemetry in this processor
        _next.Process(item);
    }

    /// <summary>
    /// Removes or redacts known-sensitive property keys from a telemetry property bag.
    /// Runs LogRedactor over all remaining values to catch any accidental leakage.
    /// </summary>
    private static void RedactProperties(IDictionary<string, string> properties)
    {
        // Remove keys that should never appear in telemetry
        foreach (var key in properties.Keys.ToList())
        {
            if (SensitivePropertyKeys.Contains(key))
            {
                properties.Remove(key);
            }
            else
            {
                properties[key] = LogRedactor.Redact(properties[key]);
            }
        }
    }

    private static void RedactRequestTelemetry(RequestTelemetry request)
    {
        var url = request.Url;
        if (url is null || string.IsNullOrEmpty(url.Query))
        {
            return;
        }

        var query = HttpUtility.ParseQueryString(url.Query);
        var modified = false;

        // Materialize keys first to avoid modifying the collection during enumeration
        var keys = query.Keys.Cast<string?>().ToList();
        foreach (var key in keys)
        {
            if (key is not null && SensitiveQueryParams.Contains(key))
            {
                query[key] = "[REDACTED]";
                modified = true;
            }
        }

        if (modified)
        {
            var builder = new UriBuilder(url)
            {
                Query = query.ToString()
            };
            request.Url = builder.Uri;
        }
    }

    private static void RedactExceptionTelemetry(ExceptionTelemetry exception)
    {
        if (exception.Exception is null)
        {
            return;
        }

        // Redact the outer message stored in telemetry properties
        exception.Message = LogRedactor.Redact(exception.Message);

        // Redact / remove sensitive custom properties
        RedactProperties(exception.Properties);
    }

    private static void RedactDependencyTelemetry(DependencyTelemetry dependency)
    {
        if (string.IsNullOrEmpty(dependency.Data))
        {
            return;
        }

        foreach (var pattern in SensitiveDependencyPatterns)
        {
            if (dependency.Data.Contains(pattern, StringComparison.OrdinalIgnoreCase))
            {
                dependency.Data = "[REDACTED — contained connection string key material]";
                return;
            }
        }
    }
}
