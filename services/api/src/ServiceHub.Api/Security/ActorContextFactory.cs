using System.Text.RegularExpressions;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Api.Security;

/// <summary>
/// The API's edge of the actor seam: turns a request into the <see cref="ActorContext"/> that
/// <see cref="IActorIdentityResolver"/> reads. <c>ServiceHub.Core</c> never sees an
/// <c>HttpContext</c>.
/// </summary>
public static partial class ActorContextFactory
{
    /// <summary>The header the SPA sends to say which browser session a request belongs to.</summary>
    public const string SessionHeaderName = "X-ServiceHub-Session";

    /// <summary>Maps the request's identity facts, as the identity middlewares recorded them.</summary>
    public static ActorContext From(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new ActorContext(
            ClaimsName: Item(context, "ClaimsName") ?? context.User.Identity?.Name,
            ApiKeyName: Item(context, "ApiKeyName"),
            Scopes: Item(context, "ApiKeyScopes"),
            SessionId: SessionId(context));
    }

    private static string? Item(HttpContext context, string key) =>
        context.Items.TryGetValue(key, out var value) && value is string text && text.Length > 0 ? text : null;

    /// <summary>
    /// The session id, only when it looks like one. It ends up in the audit trail, so anything
    /// else — a long string, markup, a newline — is dropped rather than stored.
    /// </summary>
    private static string? SessionId(HttpContext context)
    {
        var raw = context.Request.Headers[SessionHeaderName].FirstOrDefault();
        return raw is not null && ValidSession().IsMatch(raw) ? raw : null;
    }

    [GeneratedRegex("^[A-Za-z0-9-]{8,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex ValidSession();
}
