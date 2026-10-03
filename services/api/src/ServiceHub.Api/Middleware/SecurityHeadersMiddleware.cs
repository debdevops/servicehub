namespace ServiceHub.Api.Middleware;

/// <summary>
/// Sets the response headers a self-hosted single-origin application should always send.
/// </summary>
/// <remarks>
/// ServiceHub serves its SPA and its API from one origin (ADR-0014 D3), so there is no CORS to
/// configure and no cross-origin surface to widen. These headers are the rest of that posture.
/// </remarks>
public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    /// <summary>
    /// Same-origin only. The UI renders message bodies it did not write, so script is never inline
    /// and never third-party. Inline <em>styles</em> are allowed because React style attributes and
    /// the chart library use them. A self-hosted tool also loads no external fonts or CDNs.
    /// </summary>
    public const string ContentSecurityPolicy =
        "default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; " +
        "img-src 'self' data:; font-src 'self'; connect-src 'self'; object-src 'none'; " +
        "base-uri 'none'; form-action 'self'; frame-ancestors 'none'";

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var headers = context.Response.Headers;
        headers["X-Content-Type-Options"] = "nosniff";
        headers["X-Frame-Options"] = "DENY";
        headers["Referrer-Policy"] = "no-referrer";
        headers["Cross-Origin-Opener-Policy"] = "same-origin";
        headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
        headers["Content-Security-Policy"] = ContentSecurityPolicy;

        await next(context).ConfigureAwait(false);
    }
}
