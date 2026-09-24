using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// The one place an actor identity is derived. <b>Nothing constructs a
/// <see cref="RecoveryActor"/> any other way</b>, so every ledger entry, audit row and approval in
/// the product gets its name from here.
/// </summary>
/// <remarks>
/// <para>
/// This is the seam that lets Users arrive later without a refactor (ARCHITECTURE §4.4). Today
/// 4.1.0 resolves three honest outcomes; the day there is a user directory, <b>one implementation
/// changes and everything in the product becomes named.</b>
/// </para>
/// <para>
/// It takes an <see cref="ActorContext"/> rather than an <c>HttpContext</c> on purpose:
/// <c>ServiceHub.Core</c> depends on nothing, ASP.NET included. The API maps the request to an
/// <see cref="ActorContext"/> at its edge.
/// </para>
/// </remarks>
public interface IActorIdentityResolver
{
    /// <summary>
    /// Resolves who is acting. <b>Never invents a person (rule R6):</b> when nothing is
    /// configured, the honest answer is the browser session, not a name.
    /// </summary>
    RecoveryActor Resolve(ActorContext context);
}

/// <summary>
/// What the API edge knows about the caller, reduced to the facts identity resolution needs.
/// </summary>
/// <param name="ClaimsName">
/// The authenticated principal's name, when an identity provider is configured. Null otherwise —
/// and null is a normal, documented deployment, not an error.
/// </param>
/// <param name="ApiKeyName">The name of the API key presented, when one was.</param>
/// <param name="Scopes">The scopes granted to that key at request time.</param>
/// <param name="SessionId">
/// A stable-per-browser-session identifier, used only for the honest fallback: <i>"approved from
/// this browser session"</i>.
/// </param>
public sealed record ActorContext(
    string? ClaimsName = null,
    string? ApiKeyName = null,
    string? Scopes = null,
    string? SessionId = null);
