using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Results;

namespace ServiceHub.Core.Components;

/// <summary>Bounds every source obeys, so no cloud read is unbounded (PLAN-PLUG-AND-PLAY-COMPONENTS §3.5).</summary>
public static class ComponentSourceLimits
{
    /// <summary>The most places one listing returns.</summary>
    public const int MaxPlaces = 100;

    /// <summary>The most failed items one listing returns. A listing that stopped here says so (<see cref="FailedItemsPage.ReachedLimit"/>).</summary>
    public const int MaxFailedItems = 100;
}

/// <summary>What a source can honestly read. A <c>false</c> is never exceeded; the conformance kit checks it.</summary>
/// <param name="ReasonInList">Whether the list already carries each item's reason. When false the reason is read per item, on demand.</param>
/// <param name="ReadsPayload">Whether an item's input or payload can be shown at all.</param>
public sealed record ComponentSourceCapabilities(bool ReasonInList, bool ReadsPayload);

/// <summary>One place a component keeps its failures: a state machine, a workflow, a delivery container.</summary>
/// <param name="Id">Stable id the cloud uses.</param>
/// <param name="Name">The name people know it by.</param>
/// <param name="Kind">The cloud's own word for it, such as <c>state machine</c>.</param>
/// <param name="ConsoleUrl">A link to the cloud's own console, or null.</param>
public sealed record SourcePlace(string Id, string Name, string Kind, string? ConsoleUrl);

/// <summary>One failed run or delivery. A read model: nothing in it can be sent back to a cloud.</summary>
/// <param name="Id">Stable id the cloud uses for the item.</param>
/// <param name="Place">The <see cref="SourcePlace.Id"/> it belongs to.</param>
/// <param name="AtUtc">When it ended, or null when the cloud does not say.</param>
/// <param name="Status">The cloud's own status word, such as <c>FAILED</c> or <c>TIMED_OUT</c>.</param>
/// <param name="ReasonCode">The cloud's error code when the list carries it, otherwise null (not read).</param>
/// <param name="Summary">One plain line.</param>
/// <param name="Copy">Whether the failed work itself is held anywhere ServiceHub can read.</param>
/// <param name="ConsoleUrl">A link to the item in the cloud's console, or null.</param>
public sealed record FailedItem(
    string Id, string Place, DateTimeOffset? AtUtc, string Status, string? ReasonCode, string Summary, CopyKind Copy, string? ConsoleUrl);

/// <summary>A page of failed items, newest first. <see cref="ReachedLimit"/> says there are more than were returned.</summary>
public sealed record FailedItemsPage(IReadOnlyList<FailedItem> Items, bool ReachedLimit);

/// <summary>One item in full. <see cref="Reason"/> and <see cref="Cause"/> are redacted before they leave the source.</summary>
public sealed record FailedItemDetail(FailedItem Item, string? Reason, string? Cause, DateTimeOffset? StartedUtc, DateTimeOffset? StoppedUtc);

/// <summary>
/// A component that is not a message store: it reads its places and failed items, and nothing else. It can never move work on
/// (that stays behind the gate and the ledger), and it never sends anything to a cloud except list, describe and get calls.
/// </summary>
/// <remarks>
/// Compiled in and registered with one line, like a provider. <b>Proposed contract</b>: it was shaped by Step Functions and is
/// frozen only after a second source has fitted it (the two-example rule).
/// </remarks>
public interface IComponentSource
{
    /// <summary>The catalogue component it serves (<see cref="ComponentDescriptor.ComponentKey"/>).</summary>
    string ComponentKey { get; }

    /// <summary>The adapter whose connection it reads with.</summary>
    CloudProviderType Provider { get; }

    /// <summary>What it can read.</summary>
    ComponentSourceCapabilities Capabilities { get; }

    /// <summary>Whether this connection's credential may read the component. A failure's message is the reason shown ("Not enabled: ...").</summary>
    Task<Result> ValidateAccessAsync(Namespace ns, CancellationToken ct);

    /// <summary>The places, capped at <see cref="ComponentSourceLimits.MaxPlaces"/>.</summary>
    Task<Result<IReadOnlyList<SourcePlace>>> ListPlacesAsync(Namespace ns, CancellationToken ct);

    /// <summary>Failed items, newest first, capped at <see cref="ComponentSourceLimits.MaxFailedItems"/>. A null <paramref name="placeId"/> means every place.</summary>
    Task<Result<FailedItemsPage>> ListFailedAsync(Namespace ns, string? placeId, CancellationToken ct);

    /// <summary>One item in full, or a success with null when the cloud no longer has it.</summary>
    Task<Result<FailedItemDetail?>> GetFailedAsync(Namespace ns, string itemId, CancellationToken ct);
}

/// <summary>Finds the source for a component on a connection's adapter. The API depends on this, never on a module.</summary>
public interface IComponentSourceRouter
{
    /// <summary>The source for a component on an adapter, or null when none is registered (the catalogue row is then only a description).</summary>
    IComponentSource? Find(string componentKey, CloudProviderType provider);

    /// <summary>Every registered source.</summary>
    IReadOnlyList<IComponentSource> All { get; }
}
