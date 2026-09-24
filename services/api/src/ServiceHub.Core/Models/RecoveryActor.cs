using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// A resolved recovery actor — the only way an actor identity enters the Recovery Evidence
/// Ledger. No method on <c>IRecoveryLedger</c> accepts a bare, caller-supplied identity string;
/// every caller must construct this via <c>ActorIdentityResolver</c> first.
/// </summary>
/// <param name="Identity">Server-derived actor identity, e.g. <c>ApiKey:ops-bot</c>,
/// <c>user@example.com</c>, or <c>Rule:42@drain-poison-queue</c>.</param>
/// <param name="Kind">The category of actor.</param>
/// <param name="Scopes">Granted scopes at decision time, for API key actors.</param>
public sealed record RecoveryActor(string Identity, RecoveryActorKind Kind, string? Scopes = null);

/// <summary>How an actor is described to a person.</summary>
public static class RecoveryActorLabel
{
    /// <summary>The identity prefix of an actor who is only known as a browser session.</summary>
    public const string SessionPrefix = "session";

    /// <summary>The words shown for a session actor — never a name the product does not have.</summary>
    public const string SessionLabel = "from this browser session";

    /// <summary>True when the identity says nothing more than "a browser session".</summary>
    public static bool IsSession(string identity) =>
        identity.Equals(SessionPrefix, StringComparison.Ordinal)
        || identity.StartsWith(SessionPrefix + ":", StringComparison.Ordinal);

    /// <summary>The label for an identity string as stored on an audit row or ledger entry.</summary>
    public static string For(string identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return IsSession(identity) ? SessionLabel : identity;
    }
}
