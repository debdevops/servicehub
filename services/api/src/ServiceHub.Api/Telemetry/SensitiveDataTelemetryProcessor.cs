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
        "clientsecret",
        "client_secret",
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
                break;

            case ExceptionTelemetry exception:
                RedactExceptionTelemetry(exception);
                break;

            case DependencyTelemetry dependency:
                RedactDependencyTelemetry(dependency);
                break;
        }

        // Always pass the item through — never drop telemetry in this processor
        _next.Process(item);
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

        foreach (string? key in query.Keys)
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

        // Redact any custom properties that may contain secrets
        foreach (var key in exception.Properties.Keys.ToList())
        {
            exception.Properties[key] = LogRedactor.Redact(exception.Properties[key]);
        }
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
