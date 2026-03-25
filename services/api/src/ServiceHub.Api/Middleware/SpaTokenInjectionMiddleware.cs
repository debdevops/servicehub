namespace ServiceHub.Api.Middleware;

using ServiceHub.Api.Security;

/// <summary>
/// Intercepts responses that serve index.html (the SPA entry point) and injects a
/// meta tag containing a short-lived SPA token. The React app reads this token and
/// sends it as X-SPA-Token on every API request. This prevents out-of-browser replay
/// attacks (Postman, curl) because those tools never load the HTML page.
/// </summary>
public sealed class SpaTokenInjectionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly SpaTokenProvider _tokenProvider;
    private readonly ILogger<SpaTokenInjectionMiddleware> _logger;

    public SpaTokenInjectionMiddleware(RequestDelegate next, SpaTokenProvider tokenProvider, ILogger<SpaTokenInjectionMiddleware> logger)
    {
        _next = next;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Only inject for HTML page requests (not API calls, not static assets)
        if (!_tokenProvider.IsEnabled || IsApiOrAssetRequest(context.Request))
        {
            await _next(context);
            return;
        }

        // Capture the response body
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await _next(context);

        buffer.Seek(0, SeekOrigin.Begin);

        // Only inject into HTML responses (index.html served by UseDefaultFiles/MapFallbackToFile)
        var contentType = context.Response.ContentType ?? "";
        if (context.Response.StatusCode == 200 && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var html = await new StreamReader(buffer).ReadToEndAsync();

                // Inject the SPA token meta tag before </head>
                var token = _tokenProvider.GenerateToken();
                var metaTag = $"<meta name=\"spa-token\" content=\"{token}\" />";
                html = html.Replace("</head>", $"{metaTag}\n</head>", StringComparison.OrdinalIgnoreCase);

                // Write the modified response
                context.Response.Body = originalBody;
                context.Response.ContentLength = null; // Recalculate
                await context.Response.WriteAsync(html);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to inject SPA token into HTML response");
                context.Response.Body = originalBody;
                buffer.Seek(0, SeekOrigin.Begin);
                await buffer.CopyToAsync(originalBody);
            }
        }
        else
        {
            // Pass through unchanged
            context.Response.Body = originalBody;
            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(originalBody);
        }
    }

    private static bool IsApiOrAssetRequest(HttpRequest request)
    {
        var path = request.Path.Value ?? "";

        // Skip API routes
        if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            return true;

        // Skip health checks
        if (path.StartsWith("/health", StringComparison.OrdinalIgnoreCase))
            return true;

        // Skip static asset extensions
        if (HasStaticAssetExtension(path))
            return true;

        return false;
    }

    private static bool HasStaticAssetExtension(string path)
    {
        return path.EndsWith(".js", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".ico", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".woff", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".woff2", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".map", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
    }
}
