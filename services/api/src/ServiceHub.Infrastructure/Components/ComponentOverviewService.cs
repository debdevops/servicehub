using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Components;
using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Security;
using ServiceHub.Infrastructure.Dlq;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Components;

/// <summary>
/// The one computation behind Home's component table, the all-clouds cards and a component's own page (build-plan unit B1).
/// It stores nothing: a component is worked out from the adapter and the entity kind that are already stored (ADR 0020), so one
/// AWS connection yields an SQS row and an SNS row without a schema change.
/// </summary>
/// <remarks>
/// <b>No provider is named.</b> Which component an entity belongs to comes from <see cref="ComponentCatalog.ForEntity"/>; whether a
/// count exists, is approximate, or is watched comes from the adapter's <see cref="ProviderCapabilities"/>. An entity the catalogue
/// does not know goes into an "Other" component of class <see cref="RecoverabilityClass.R0Unknown"/> — it is never dropped.
/// Nothing here receives or pulls a message: it lists entities, reads recorded rows and reads pending work.
/// </remarks>
public sealed class ComponentOverviewService : IComponentOverviewService
{
    /// <summary>The key of the component that holds entities the catalogue does not know.</summary>
    public const string UnknownComponentKey = "unknown";

    private const int PendingWorkLimit = 500;
    private const string SubscriptionSegment = "/subscriptions/";

    private readonly ServiceHubDbContext _db;
    private readonly INamespaceRepository _namespaces;
    private readonly ICloudProviderRouter _router;
    private readonly IPendingWorkService _pending;
    private readonly TimeProvider _time;

    /// <summary>Creates the service.</summary>
    public ComponentOverviewService(
        ServiceHubDbContext db, INamespaceRepository namespaces, ICloudProviderRouter router, IPendingWorkService pending, TimeProvider? time = null)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _router = router ?? throw new ArgumentNullException(nameof(router));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _time = time ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<ComponentOverview> GetAsync(string ownerId, IReadOnlySet<Guid>? allowedNamespaceIds, CloudKind? cloud, CancellationToken cancellationToken)
    {
        var rows = await BuildAsync(ownerId, allowedNamespaceIds, only: null, cloud, cancellationToken).ConfigureAwait(false);
        return new ComponentOverview(rows, _time.GetUtcNow());
    }

    /// <inheritdoc />
    public async Task<ConnectedComponent?> GetOneAsync(
        string ownerId, IReadOnlySet<Guid>? allowedNamespaceIds, Guid namespaceId, string componentKey, CancellationToken cancellationToken)
    {
        var rows = await BuildAsync(ownerId, allowedNamespaceIds, namespaceId, cloud: null, cancellationToken).ConfigureAwait(false);
        return rows.FirstOrDefault(c => string.Equals(c.ComponentKey, componentKey, StringComparison.Ordinal));
    }

    private async Task<IReadOnlyList<ConnectedComponent>> BuildAsync(
        string ownerId, IReadOnlySet<Guid>? allowedNamespaceIds, Guid? only, CloudKind? cloud, CancellationToken cancellationToken)
    {
        var listed = await _namespaces.GetByOwnerAsync(ownerId, allowedNamespaceIds, cancellationToken).ConfigureAwait(false);
        var connected = listed.IsSuccess
            ? listed.Value
                .Where(n => n.IsActive && _router.IsRegistered(n.Provider) && (only is null || n.Id == only))
                .Where(n => ComponentCatalog.CloudOf(n.Provider) is { } c && (cloud is null || c == cloud))
                .ToList()
            : [];
        if (connected.Count == 0)
        {
            return [];
        }

        var ids = connected.Select(n => n.Id).ToList();

        // Recorded dead letters, per namespace and entity. "Never looked" is the absence of any row at all.
        var recorded = await _db.DlqMessages.AsNoTracking()
            .Where(m => m.OwnerId == ownerId && ids.Contains(m.NamespaceId) && m.Status != DlqMessageStatus.Archived)
            .Select(m => new RecordedRow(m.NamespaceId, m.EntityName, m.Status))
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var pending = await _pending.ListAsync(new PendingWorkScope(ownerId, allowedNamespaceIds), PendingWorkLimit, cancellationToken).ConfigureAwait(false);
        var work = pending.Items.Where(i => i.NamespaceId is { } id && ids.Contains(id) && i.Kind is "approval" or "unresolved").ToList();

        var reads = await Task.WhenAll(connected.Select(n => ReadAsync(n, cancellationToken))).ConfigureAwait(false);

        var result = new List<ConnectedComponent>();
        foreach (var (ns, entities, error, noStore) in reads)
        {
            // A connection with no store credential (an Azure sign-in connection, made only to read Logic Apps or Event Grid) has no queues
            // to count. Listing a "Service Bus: can't reach" row for it would say something is broken when nothing is.
            if (noStore)
            {
                continue;
            }

            var caps = _router.Resolve(ns.Provider).Capabilities;
            var kind = ComponentCatalog.CloudOf(ns.Provider)!.Value;
            var mine = recorded.Where(m => m.NamespaceId == ns.Id).ToList();
            var itemsHere = work.Where(i => i.NamespaceId == ns.Id).ToList();
            var now = _time.GetUtcNow();

            if (entities is null)
            {
                // Unreadable: no entities are known, so one row per component this adapter covers says so, with no numbers.
                var keys = CoveredBy(ns);
                for (var i = 0; i < keys.Count; i++)
                {
                    // Pending work is not split by component here (no entity is known), so it is shown once, on the first row.
                    result.Add(Row(ns, kind, keys[i], caps, [], i == 0 ? itemsHere : [], error, observed: null, unreadable: true));
                }

                continue;
            }

            // A queue another entity names as its dead-letter target holds dead letters; its messages are already counted
            // against the source, so listing it as a place of its own would show every dead letter twice (as /stats does).
            var targets = entities.Select(e => e.DeadLetterTargetName).Where(t => !string.IsNullOrEmpty(t)).ToHashSet(StringComparer.Ordinal);
            var holders = entities
                .Where(e => CloudEntityKinds.Of(e) is "queue" or "subscription" && !targets.Contains(e.Name))
                .OrderBy(e => CloudEntityKinds.Of(e), StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.Ordinal)
                .ToList();

            if (holders.Count == 0)
            {
                // Connected and readable, but holding no queue or subscription: it is still connected, so its components are shown
                // (reachable, no entities) rather than vanishing as if nothing had been connected.
                foreach (var d in CoveredBy(ns))
                {
                    result.Add(Row(ns, kind, d, caps, [], [], null, now, unreadable: false));
                }

                continue;
            }

            foreach (var group in holders.GroupBy(e => ComponentCatalog.ForEntity(ns.Provider, CloudEntityKinds.Of(e))?.ComponentKey ?? UnknownComponentKey))
            {
                var descriptor = group.Key == UnknownComponentKey ? null : ComponentCatalog.ForEntity(ns.Provider, CloudEntityKinds.Of(group.First()));
                var rows = group.Select(e => Entity(e, caps, mine.Where(m => m.EntityName == e.Name).ToList(), itemsHere, now)).ToList();
                var withinGroup = itemsHere.Where(i => rows.Any(r => Matches(i, r))).ToList();
                result.Add(Row(ns, kind, descriptor, caps, rows, withinGroup, null, now, unreadable: false));
            }

            // Pending work whose entity is not in any listed row (the queue was since deleted) still needs a person: it joins the
            // namespace's first component rather than being dropped.
            var placed = result.Where(c => c.NamespaceId == ns.Id).ToList();
            var stray = itemsHere.Where(i => !placed.Any(c => c.Entities.Any(e => Matches(i, e)))).ToList();
            if (stray.Count > 0 && placed.Count > 0)
            {
                var first = placed[0];
                var index = result.IndexOf(first);
                result[index] = first with
                {
                    NeedsYou = first.NeedsYou + stray.Count,
                    Approvals = first.Approvals + stray.Count(i => i.Kind == "approval"),
                    HandOffs = first.HandOffs + stray.Count(i => i.Kind == "unresolved"),
                };
            }
        }

        return [.. result.OrderBy(c => c.Cloud).ThenBy(c => c.Name, StringComparer.Ordinal).ThenBy(c => c.ComponentKey, StringComparer.Ordinal)];
    }

    /// <summary>The components the catalogue says this connection's adapter covers, one per component key.</summary>
    private static List<ComponentDescriptor> CoveredBy(Namespace ns) => ComponentCatalog.All
        .Where(c => c.Provider == ns.Provider && c.Detection == DetectionKind.Store && c.Status == SupportStatus.Supported)
        .DistinctBy(c => c.ComponentKey).ToList();

    private static bool Matches(PendingWorkItem item, ComponentEntity entity) =>
        string.Equals(item.Entity, entity.Parent is null ? entity.Name : $"{entity.Parent}{SubscriptionSegment}{entity.Name}", StringComparison.Ordinal)
        || string.Equals(item.Entity, entity.Name, StringComparison.Ordinal);

    private async Task<(Namespace Ns, IReadOnlyList<CloudEntity>? Entities, string? Error, bool NoStore)> ReadAsync(Namespace ns, CancellationToken cancellationToken)
    {
        try
        {
            var listed = await _router.Resolve(ns.Provider).ListEntitiesAsync(ns.Id, cancellationToken).ConfigureAwait(false);
            if (listed.IsSuccess)
            {
                return (ns, listed.Value, null, false);
            }

            return (ns, null, Safe(listed.Error.Message), listed.Error.Code == ErrorCodes.Namespace.ConnectionStringRequired);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031 // One unreachable cloud must not take down the whole table: it becomes a "Can't reach" row.
        catch (Exception ex)
        {
            return (ns, null, Safe(ex.Message), false);
        }
#pragma warning restore CA1031
    }

    private sealed record RecordedRow(Guid NamespaceId, string EntityName, DlqMessageStatus Status);

    // A cloud's error text goes to the screen, so keys are masked and control characters dropped (SanitiseForLog alone strips only the latter).
    private static string Safe(string? message) => LogRedactor.SanitiseForLog(LogRedactor.Redact(message));

    private ComponentEntity Entity(
        CloudEntity e, ProviderCapabilities caps, IReadOnlyList<RecordedRow> recordedHere, IReadOnlyList<PendingWorkItem> items, DateTimeOffset now)
    {
        var name = e.Name;
        string? parent = null;
        var at = name.IndexOf(SubscriptionSegment, StringComparison.Ordinal);
        if (at > 0)
        {
            parent = name[..at];
            name = name[(at + SubscriptionSegment.Length)..];
        }

        var watched = caps.SupportsRepeatablePeek;
        var active = recordedHere.Count(m => m.Status == DlqMessageStatus.Active);
        var mine = items.Count(i => string.Equals(i.Entity, e.Name, StringComparison.Ordinal));
        return new ComponentEntity(
            name, parent, CloudEntityKinds.Of(e), e.DeadLetterTargetName, NoStoreReason: null,
            DeadLetterCount(e, caps),
            watched ? null : new LastLook(recordedHere.Count == 0 ? null : active, active >= DlqScanner.MaxMessagesPerEntityScan, AtUtc: null),
            mine, now);
    }

    private static Count DeadLetterCount(CloudEntity e, ProviderCapabilities caps) =>
        !caps.SupportsMessageCounts ? new Count(null, CountKind.NotReported)
        : new Count(e.DeadLetterCount, caps.CountsAreApproximate ? CountKind.Approximate : CountKind.Exact);

    private ConnectedComponent Row(
        Namespace ns, CloudKind cloud, ComponentDescriptor? descriptor, ProviderCapabilities caps, IReadOnlyList<ComponentEntity> entities,
        IReadOnlyList<PendingWorkItem> items, string? error, DateTimeOffset? observed, bool unreadable)
    {
        var watched = caps.SupportsRepeatablePeek;
        Count deadLetters;
        if (unreadable)
        {
            deadLetters = new Count(null, CountKind.NotRead);
        }
        else if (!caps.SupportsMessageCounts)
        {
            deadLetters = new Count(null, CountKind.NotReported);
        }
        else
        {
            deadLetters = new Count(entities.Sum(e => e.DeadLetters.Value ?? 0), caps.CountsAreApproximate ? CountKind.Approximate : CountKind.Exact);
        }

        LastLook? recorded = null;
        if (!watched)
        {
            var kept = entities.Where(e => e.Recorded?.Messages is not null).ToList();
            recorded = new LastLook(kept.Count == 0 ? null : kept.Sum(e => e.Recorded!.Messages!.Value), entities.Any(e => e.Recorded?.ReachedLimit == true), AtUtc: null);
        }

        var approvals = items.Count(i => i.Kind == "approval");
        var handOffs = items.Count(i => i.Kind == "unresolved");
        return new ConnectedComponent(
            ns.Id, descriptor?.ComponentKey ?? UnknownComponentKey, cloud, ns.DisplayName ?? ns.Name, descriptor?.Name ?? "Other",
            ns.Environment, CanSend: ns.CanSend != false, Reachable: !unreadable, error, watched,
            descriptor?.Class ?? RecoverabilityClass.R0Unknown,
            deadLetters, recorded, approvals + handOffs, approvals, handOffs, observed, entities);
    }
}
