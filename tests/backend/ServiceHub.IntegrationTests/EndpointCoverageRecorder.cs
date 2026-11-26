using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace ServiceHub.IntegrationTests;

/// <summary>
/// Records which API endpoints the integration tests actually call, and which exist, so
/// <c>tests/scripts/check-endpoint-coverage.sh</c> can fail the build when an endpoint ships with no test hitting it.
/// A test host cannot assert "every other test has run" (xUnit 2 has no end-of-run hook that fails a run), so the
/// facts are written to <c>endpoint-coverage.json</c> when the test process exits and checked afterwards.
/// </summary>
internal static class EndpointCoverageRecorder
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> Hits = new(StringComparer.Ordinal);
    private static HashSet<string>? _all;

    static EndpointCoverageRecorder() => AppDomain.CurrentDomain.ProcessExit += (_, _) => Write();

    /// <summary>Output path; the check script reads it. Overridable so CI can collect it from a known place.</summary>
    internal static string OutputPath =>
        Environment.GetEnvironmentVariable("SERVICEHUB_ENDPOINT_COVERAGE_FILE")
        ?? Path.Combine(AppContext.BaseDirectory, "endpoint-coverage.json");

    /// <summary>Middleware at the very front of the pipeline; reads the matched endpoint once the request has been handled.</summary>
    internal static void Use(IApplicationBuilder app)
    {
        var services = app.ApplicationServices;
        app.Use(async (context, next) =>
        {
            await next();
            if (context.GetEndpoint() is RouteEndpoint endpoint)
            {
                Record(services, context.Request.Method, endpoint);
            }
        });
    }

    private static void Record(IServiceProvider services, string method, RouteEndpoint endpoint)
    {
        lock (Gate)
        {
            _all ??= services.GetServices<EndpointDataSource>()
                .SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>()
                .SelectMany(Keys)
                .ToHashSet(StringComparer.Ordinal);
            Hits.Add($"{method.ToUpperInvariant()} {Normalise(endpoint.RoutePattern.RawText)}");
        }
    }

    private static IEnumerable<string> Keys(RouteEndpoint endpoint)
    {
        var methods = endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods;
        if (methods is null || endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Mvc.Controllers.ControllerActionDescriptor>() is null)
        {
            yield break; // only controller actions are product endpoints; static files, health and the SPA fallback are not
        }

        foreach (var method in methods)
        {
            yield return $"{method.ToUpperInvariant()} {Normalise(endpoint.RoutePattern.RawText)}";
        }
    }

    private static string Normalise(string? pattern) => "/" + (pattern ?? string.Empty).Trim('/');

    private static void Write()
    {
        lock (Gate)
        {
            if (_all is null)
            {
                return;
            }

            var document = new { all = _all.Order().ToArray(), hit = Hits.Intersect(_all).Order().ToArray() };
            File.WriteAllText(OutputPath, JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
