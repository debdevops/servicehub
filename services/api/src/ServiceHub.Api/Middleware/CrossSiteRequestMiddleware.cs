using ServiceHub.Core.Constants;

namespace ServiceHub.Api.Middleware;

/// <summary>
/// Refuses a request that CHANGES something when a browser says another website sent it.
/// </summary>
/// <remarks>
/// <para>ServiceHub does not log the browser in, so on its own machine every request is the owner's. A page on any other site can make the
/// owner's browser send a request to <c>http://localhost:…</c>; it cannot read the answer, but the request arrives. Most changing endpoints
/// already need the <c>X-ServiceHub-Intent</c> header or a JSON body, which a cross-site page cannot send without a permission check the
/// browser fails. A few take no body and no intent (pause an agent, cancel a bulk run, test a connection): those were reachable.</para>
/// <para>Every current browser labels a request with <c>Sec-Fetch-Site</c>. <c>same-origin</c> is ServiceHub's own page and <c>none</c> is the
/// person typing an address; anything else (<c>cross-site</c>, <c>same-site</c>) is another page. A browser too old to send it still sends
/// <c>Origin</c> on a changing request, and that is compared with the host it was sent to. A request with neither header is not from a web
/// page (curl, a script, the MCP tool, a platform probe) and is let through: those callers are governed by API keys and roles, not by this.</para>
/// <para>Reads (GET, HEAD, OPTIONS) are never refused here: they change nothing and a cross-site page cannot see their answers.</para>
/// </remarks>
public sealed class CrossSiteRequestMiddleware
{
    private readonly RequestDelegate _next;

    /// <summary>Creates the middleware.</summary>
    public CrossSiteRequestMiddleware(RequestDelegate next) => _next = next ?? throw new ArgumentNullException(nameof(next));

    /// <summary>
    /// True when a changing request must be refused. <paramref name="fetchSite"/> and <paramref name="origin"/> are the request's
    /// <c>Sec-Fetch-Site</c> and <c>Origin</c> headers (null when absent); <paramref name="host"/> is the host name it was sent to, no port.
    /// </summary>
    public static bool IsCrossSite(string method, string? fetchSite, string? origin, string host)
    {
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(fetchSite))
        {
            return !(fetchSite.Equals("same-origin", StringComparison.OrdinalIgnoreCase) || fetchSite.Equals("none", StringComparison.OrdinalIgnoreCase));
        }

        if (string.IsNullOrWhiteSpace(origin))
        {
            return false;
        }

        // "null" is what a sandboxed frame or a file: page sends. It is not ServiceHub's page.
        return !Uri.TryCreate(origin, UriKind.Absolute, out var from) || !string.Equals(from.Host.Trim('[', ']'), host.Trim('[', ']'), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var request = context.Request;

        if (!IsCrossSite(request.Method, request.Headers["Sec-Fetch-Site"].ToString(), request.Headers.Origin.ToString(), request.Host.Host))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        await Results.Problem(
            title: "This request came from another website",
            detail: "ServiceHub changes something only when its own page, or a tool that is not a web page, asks. " +
                    "A page on another site sent this request through your browser, so it was refused and nothing was changed.",
            statusCode: StatusCodes.Status403Forbidden,
            extensions: new Dictionary<string, object?>
            {
                ["code"] = ErrorCodes.CrossSiteRequest,
                ["correlationId"] = context.TraceIdentifier,
            }).ExecuteAsync(context).ConfigureAwait(false);
    }
}
