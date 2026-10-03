using ServiceHub.Core.Constants;

namespace ServiceHub.Api.Middleware;

/// <summary>
/// Answers only to the host names this ServiceHub is meant to be reached by — by default <c>localhost</c>, <c>127.0.0.1</c> and <c>[::1]</c>.
/// </summary>
/// <remarks>
/// <para>ServiceHub does not log the browser in: whoever can reach the port is the server's owner, an Administrator. That is safe on a loopback
/// address only if a web page cannot reach it either, and one can — <b>DNS rebinding</b>: a page on <c>evil.example</c> re-points its own name at
/// <c>127.0.0.1</c>, after which the browser treats ServiceHub as that page's own origin and lets its script call the whole API (setting the
/// intent header is no obstacle to same-origin script). The request still carries <c>Host: evil.example</c>, so refusing hosts that are not
/// ours closes it.</para>
/// <para><c>AllowedHosts</c> (semicolon- or comma-separated, <c>*.example.com</c> wildcards, <c>*</c> for anything) overrides the default; set it
/// when ServiceHub is deliberately reached by another name — a reverse proxy, an App Service host name, a LAN address. The health endpoints
/// answer on any host: they say only "Healthy", and a platform probe often calls them by an internal address.</para>
/// </remarks>
public sealed class HostAllowListMiddleware
{
    /// <summary>What is allowed when <c>AllowedHosts</c> is not set.</summary>
    public static readonly IReadOnlyList<string> LoopbackHosts = ["localhost", "127.0.0.1", "[::1]"];

    private readonly RequestDelegate _next;
    private readonly string[] _allowed;
    private readonly bool _any;

    /// <summary>Creates the middleware from the <c>AllowedHosts</c> setting.</summary>
    public HostAllowListMiddleware(RequestDelegate next, IConfiguration configuration)
    {
        _next = next ?? throw new ArgumentNullException(nameof(next));
        ArgumentNullException.ThrowIfNull(configuration);
        var configured = configuration["AllowedHosts"];
        var entries = string.IsNullOrWhiteSpace(configured)
            ? [.. LoopbackHosts]
            : configured.Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _any = entries.Contains("*");
        _allowed = entries;
    }

    /// <summary>True when <paramref name="host"/> (no port) is allowed by <paramref name="allowed"/>: an exact name, or <c>*.suffix</c>.</summary>
    public static bool Matches(IEnumerable<string> allowed, string host) =>
        !string.IsNullOrEmpty(host) && allowed.Any(a =>
            string.Equals(a, host, StringComparison.OrdinalIgnoreCase)
            || (a.StartsWith("*.", StringComparison.Ordinal) && host.EndsWith(a[1..], StringComparison.OrdinalIgnoreCase) && host.Length > a.Length - 1));

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (_any || context.Request.Path.StartsWithSegments("/health") || Matches(_allowed, context.Request.Host.Host))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await Results.Problem(
            title: "This address is not one ServiceHub answers to",
            detail: "ServiceHub answers only to localhost by default, so a web page on another site cannot drive it through your browser. " +
                    "If you reach it by another name (a reverse proxy, a host name, a LAN address), set AllowedHosts to that name.",
            statusCode: StatusCodes.Status400BadRequest,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ErrorCodes.HostNotAllowed,
                ["correlationId"] = context.TraceIdentifier,
            }).ExecuteAsync(context).ConfigureAwait(false);
    }
}
