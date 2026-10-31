using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;

namespace ServiceHub.Core.Components;

/// <summary>What a cloud said when it accepted a request to move a failed run on.</summary>
/// <param name="AcceptedAtUtc">When the cloud says it took the request, or null when it does not say.</param>
public sealed record MoveOnAcceptance(DateTimeOffset? AcceptedAtUtc);

/// <summary>
/// Asks a cloud to move one failed run of a component on, by the cloud's own redrive (build-plan unit PP5). <b>Separate from
/// <see cref="IComponentSource"/> on purpose</b>, so a read-only source can never move anything by mistake. Nothing may hold this
/// except the gated service (<c>ComponentRedriveService</c>); the kernel guard checks it.
/// </summary>
/// <remarks>
/// The cloud restarts the run itself, so ServiceHub never reads the run's input. What the cloud accepted is all that is known: it says
/// nothing about whether the run then finished fixed.
/// </remarks>
public interface IComponentMoveOn
{
    /// <summary>The catalogue component it serves.</summary>
    string ComponentKey { get; }

    /// <summary>The adapter whose connection it acts with.</summary>
    CloudProviderType Provider { get; }

    /// <summary>What the cloud calls the action, lower case, for the words people read: <c>redrive</c> or <c>resubmit</c>.</summary>
    string Verb { get; }

    /// <summary>Whether <paramref name="itemId"/> is a run of this component in this connection's own account and region. Checked before anything is recorded or sent.</summary>
    bool Owns(Namespace ns, string itemId);

    /// <summary>The place (such as the state machine) a run belongs to, worked out from its id alone. Only called after <see cref="Owns"/> said yes.</summary>
    string PlaceOf(string itemId);

    /// <summary>
    /// Asks the cloud to move the run on. A success means the cloud accepted the request. A failure that cannot have reached the cloud
    /// (validation, not found, forbidden, conflict) is told apart from one that may have (anything else) by its <see cref="Error.Type"/>.
    /// </summary>
    /// <param name="ns">The connection.</param>
    /// <param name="itemId">The run's id.</param>
    /// <param name="requestToken">A token the cloud uses to answer a repeated request once; the ledger entry's id.</param>
    /// <param name="ct">Cancellation.</param>
    Task<Result<MoveOnAcceptance>> RedriveAsync(Namespace ns, string itemId, string requestToken, CancellationToken ct);
}
