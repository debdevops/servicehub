namespace ServiceHub.Api.Middleware;

/// <summary>
/// Gives every request a correlation id, echoes it back, and puts it on the log scope — so one
/// identifier ties a browser request, a log line, an event and a ledger entry together.
/// </summary>
public sealed class CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
{
    /// <summary>The request and response header carrying the correlation id.</summary>
    public const string HeaderName = "X-Correlation-Id";

    /// <summary>Runs the middleware.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        // A caller-supplied id is echoed into a response header and every log line, so only a short,
        // plain one is trusted. Anything else is replaced, not rejected — correlation is a courtesy.
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var supplied)
                            && IsAcceptable(supplied.ToString())
            ? supplied.ToString()
            : context.TraceIdentifier;

        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId }))
        {
            await next(context).ConfigureAwait(false);
        }
    }

    /// <summary>The longest caller-supplied correlation id that is accepted as-is.</summary>
    public const int MaxLength = 64;

    internal static bool IsAcceptable(string value) =>
        value.Length is > 0 and <= MaxLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or ':');
}
