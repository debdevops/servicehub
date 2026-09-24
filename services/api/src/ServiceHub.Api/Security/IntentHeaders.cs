namespace ServiceHub.Api.Security;

/// <summary>
/// The header that says a caller meant a dangerous request. A delete cannot be triggered by a stray
/// prefetch, a link crawler or a URL pasted out of DevTools, because none of them send it.
/// </summary>
/// <remarks>Not authentication — it closes the common accident, not a determined attacker.</remarks>
public static class IntentHeaders
{
    /// <summary>The header's name; the SPA sends the same one (<c>lib/api/intentHeaders.ts</c>).</summary>
    public const string HeaderName = "X-ServiceHub-Intent";

    /// <summary>Connecting a namespace stores a credential.</summary>
    public const string CreateNamespace = "create-namespace";

    /// <summary>Removing a namespace forgets a credential and everything watched through it.</summary>
    public const string DeleteNamespace = "delete-namespace";

    /// <summary>Intent for replaying one dead letter.</summary>
    public const string ReplayMessage = "replay-message";

    /// <summary>Whether the request declared exactly <paramref name="expected"/>.</summary>
    public static bool Declares(HttpRequest request, string expected)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.Headers.TryGetValue(HeaderName, out var values)
            && string.Equals(values.ToString().Trim(), expected, StringComparison.Ordinal);
    }

    /// <summary>The sentence a caller reads when the header is missing: it names the fix.</summary>
    public static string MissingDetail(string action, string expected) =>
        $"Confirm you meant to {action} by sending the header '{HeaderName}: {expected}'.";
}
