using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Components;

/// <summary>
/// CONTRACT ONLY — build-plan unit 3.1b implements it in <c>ServiceHub.Infrastructure/Components/ComponentOverviewService.cs</c>.
/// The one computation behind Home's component table, the all-clouds cards and a component's own page, so the three can
/// never show different numbers (the same "one number, computed once" rule as <c>/recovery/summary</c>).
/// </summary>
/// <remarks>
/// It composes what already exists and stores nothing new: namespaces (<c>INamespaceRepository</c>), entities and their counts
/// (the provider router), recorded dead letters (<c>IDlqMessageReader</c>) and pending work (<c>IPendingWorkService</c>).
/// A top row is a connection as it is stored today (a namespace); its component comes from
/// <see cref="ComponentCatalog.ForEntity"/>. One AWS connection therefore yields two rows (SQS and SNS) with no schema change.
/// </remarks>
public interface IComponentOverviewService
{
    /// <summary>Every connected component the caller may see, optionally for one cloud.</summary>
    Task<ComponentOverview> GetAsync(string ownerId, IReadOnlySet<Guid>? allowedNamespaceIds, CloudKind? cloud, CancellationToken cancellationToken);

    /// <summary>One connected component with all of its entities, or null when the caller may not see it.</summary>
    Task<ConnectedComponent?> GetOneAsync(string ownerId, IReadOnlySet<Guid>? allowedNamespaceIds, Guid namespaceId, string componentKey, CancellationToken cancellationToken);
}

/// <summary>How a count should be read. Never a bare number that might be mistaken for exact.</summary>
public enum CountKind
{
    /// <summary>The cloud reports an exact number.</summary>
    Exact = 0,

    /// <summary>The cloud reports an approximate number.</summary>
    Approximate = 1,

    /// <summary>The cloud reports no number. The value is null and the screen says "Can't count here".</summary>
    NotReported = 2,

    /// <summary>The cloud could report it, but the read failed or has not happened. The value is null.</summary>
    NotRead = 3,
}

/// <summary>A number together with how far it can be trusted.</summary>
public sealed record Count(long? Value, CountKind Kind);

/// <summary>What ServiceHub recorded the last time it looked, on clouds it does not watch.</summary>
/// <param name="Messages">How many it kept. Null when it has never looked.</param>
/// <param name="ReachedLimit">True when the look stopped at its limit, so the real number may be higher.</param>
/// <param name="AtUtc">When it looked. Null when it has never looked.</param>
public sealed record LastLook(int? Messages, bool ReachedLimit, DateTimeOffset? AtUtc);

/// <summary>One place messages get stuck: a queue, a subscription, a subscriber queue.</summary>
/// <param name="Name">The entity's own name.</param>
/// <param name="Parent">The topic a subscription belongs to, when it has one.</param>
/// <param name="Kind"><c>queue</c>, <c>topic</c> or <c>subscription</c>.</param>
/// <param name="DeadLetterStore">The dead-letter queue or topic's own name, when it differs from the entity. Null with <paramref name="NoStoreReason"/> set when there is none.</param>
/// <param name="NoStoreReason">Why nothing can be read here, in words ("No dead-letter queue set").</param>
/// <param name="DeadLetters">What the cloud reports is in dead-letter now.</param>
/// <param name="Recorded">What ServiceHub kept at the last look. Null on clouds that are watched automatically.</param>
/// <param name="NeedsYou">Approvals plus hand-offs waiting on this entity.</param>
/// <param name="ObservedAtUtc">When this row was last read.</param>
public sealed record ComponentEntity(
    string Name, string? Parent, string Kind, string? DeadLetterStore, string? NoStoreReason,
    Count DeadLetters, LastLook? Recorded, int NeedsYou, DateTimeOffset? ObservedAtUtc);

/// <summary>
/// One connected component: one top row on Home. <c>NamespaceId</c> is the stored connection it comes from and
/// <c>ComponentKey</c> says which component of that connection. <c>CanSend</c> is false for a read-only connection
/// (everything is shown, no replay is offered; unit 1.2). <c>Reachable</c> is false when the last read failed and
/// <c>LastError</c> says why. <c>Watched</c> is true where ServiceHub looks by itself and false where a person presses Look now.
/// </summary>
public sealed record ConnectedComponent(
    Guid NamespaceId, string ComponentKey, CloudKind Cloud, string Name, string ComponentName, EnvironmentType Environment,
    bool CanSend, bool Reachable, string? LastError, bool Watched, RecoverabilityClass Class,
    Count DeadLetters, LastLook? Recorded, int NeedsYou, int Approvals, int HandOffs, DateTimeOffset? ObservedAtUtc,
    IReadOnlyList<ComponentEntity> Entities);

/// <summary>Everything Home shows about connected components. Totals are per cloud and are never added across clouds (R5).</summary>
public sealed record ComponentOverview(IReadOnlyList<ConnectedComponent> Components, DateTimeOffset GeneratedAtUtc);
