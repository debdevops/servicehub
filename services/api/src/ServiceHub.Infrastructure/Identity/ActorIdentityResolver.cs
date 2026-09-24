using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Identity;

/// <summary>
/// The one place an actor is named. Three honest outcomes (rule R6), most specific first:
/// <list type="number">
/// <item>An API key presented by the caller — named, and visibly a credential (<c>ApiKey:ops-bot</c>).</item>
/// <item>An identity provider's name for the person, when one is configured.</item>
/// <item>Otherwise, only <i>"from this browser session"</i>. The product does not invent a person.</item>
/// </list>
/// </summary>
/// <remarks>
/// When Users arrives, this is the one implementation that changes; every audit row and ledger
/// entry becomes named without anything else moving (ARCHITECTURE §4.4).
/// </remarks>
public sealed class ActorIdentityResolver : IActorIdentityResolver
{
    /// <summary>Prefix every API-key actor carries, so a grant for a key can only ever match a key.</summary>
    public const string ApiKeyIdentityPrefix = "ApiKey:";

    /// <inheritdoc />
    public RecoveryActor Resolve(ActorContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!string.IsNullOrWhiteSpace(context.ApiKeyName))
        {
            return new RecoveryActor($"{ApiKeyIdentityPrefix}{context.ApiKeyName.Trim()}", RecoveryActorKind.ApiKey, context.Scopes);
        }

        if (!string.IsNullOrWhiteSpace(context.ClaimsName))
        {
            return new RecoveryActor(context.ClaimsName.Trim(), RecoveryActorKind.User, context.Scopes);
        }

        // Nothing configured — a valid, documented deployment, not an error. Say only what is known.
        var identity = string.IsNullOrWhiteSpace(context.SessionId)
            ? RecoveryActorLabel.SessionPrefix
            : $"{RecoveryActorLabel.SessionPrefix}:{context.SessionId.Trim()}";
        return new RecoveryActor(identity, RecoveryActorKind.User);
    }
}
