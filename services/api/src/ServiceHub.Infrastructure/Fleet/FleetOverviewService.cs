using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Fleet;

/// <summary>
/// The fleet read model. Every number comes from a real source: dead letters ServiceHub recorded (new and resolved in
/// the window, what is stuck now), the replay ledger (what came back), and each namespace's capabilities.
/// </summary>
/// <remarks>
/// <b>No number is summed across clouds</b> and no provider name is tested: "can it confirm a fix" is
/// <c>CanProveDlqAbsence</c>; "does it look on its own" is <c>SupportsRepeatablePeek</c>. Where a cloud is not watched,
/// ServiceHub has recorded nothing, so the counts are reported as absent and health as "cannot tell" — silence is not health.
/// </remarks>
public sealed class FleetOverviewService : IFleetOverviewService
{
    private const int TopFailureRows = 8;

    private readonly ServiceHubDbContext _db;
    private readonly INamespaceRepository _namespaces;
    private readonly ICloudProviderRouter _router;

    /// <summary>Creates the service.</summary>
    public FleetOverviewService(ServiceHubDbContext db, INamespaceRepository namespaces, ICloudProviderRouter router)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _namespaces = namespaces ?? throw new ArgumentNullException(nameof(namespaces));
        _router = router ?? throw new ArgumentNullException(nameof(router));
    }

    /// <inheritdoc />
    public async Task<FleetOverview> GetAsync(
        string ownerId, IReadOnlySet<Guid>? allowedNamespaceIds, string window, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var listed = await _namespaces.GetByOwnerAsync(ownerId, allowedNamespaceIds, cancellationToken).ConfigureAwait(false);
        var connected = listed.IsSuccess ? listed.Value.Where(n => n.IsActive && _router.IsRegistered(n.Provider)).ToList() : [];
        var ids = connected.Select(n => n.Id).ToList();

        var messages = await _db.DlqMessages.AsNoTracking()
            .Where(m => m.OwnerId == ownerId && ids.Contains(m.NamespaceId))
            .Select(m => new { m.NamespaceId, m.CloudProvider, m.Status, m.DeadLetterReason, m.DetectedAtUtc, m.ResolvedAt })
            .ToListAsync(cancellationToken).ConfigureAwait(false);

        var returned = (await _db.RecoveryLedgerEntries.AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.State == RecoveryEntryState.Returned && e.NamespaceId != null)
            .Select(e => e.NamespaceId!.Value).Distinct().ToListAsync(cancellationToken).ConfigureAwait(false)).ToHashSet();

        var rows = new List<FleetNamespace>();
        foreach (var ns in connected)
        {
            var caps = _router.Resolve(ns.Provider).Capabilities;
            var mine = messages.Where(m => m.NamespaceId == ns.Id).ToList();
            var active = mine.Where(m => m.Status == DlqMessageStatus.Active).ToList();
            var top = active.GroupBy(m => m.DeadLetterReason ?? "Unknown")
                .Select(g => new FleetFailure(g.Key, g.Count())).OrderByDescending(f => f.Count).ThenBy(f => f.Reason, StringComparer.Ordinal).FirstOrDefault();

            var watched = caps.SupportsRepeatablePeek;
            var health = ns.LastConnectionTestSucceeded == false || returned.Contains(ns.Id) ? FleetHealth.NeedsALook
                : watched ? FleetHealth.Healthy
                : FleetHealth.CannotTell;

            rows.Add(new FleetNamespace(
                ns.Id, ns.Name, ns.DisplayName, ns.Provider, ns.Environment, watched,
                watched ? active.Count : null,
                watched ? mine.Count(m => m.DetectedAtUtc >= since) : 0,
                watched ? mine.Count(m => m.ResolvedAt is { } r && r >= since) : 0,
                top, health));
        }

        var clouds = connected.GroupBy(n => n.Provider).Select(g =>
        {
            var members = rows.Where(r => r.Provider == g.Key).ToList();
            var allProve = g.All(n => _router.Resolve(n.Provider).Capabilities.CanProveDlqAbsence);
            var watched = members.All(r => r.Watched);
            return new FleetCloud(
                g.Key, g.Count(), allProve ? FleetCapabilityState.CanConfirm : FleetCapabilityState.ObserverRequired, watched,
                watched ? members.Sum(r => r.Active ?? 0) : null,
                members.Sum(r => r.NewInWindow), members.Sum(r => r.ResolvedInWindow));
        }).OrderBy(c => c.Provider).ToList();

        var topFailures = messages.Where(m => m.Status == DlqMessageStatus.Active)
            .GroupBy(m => new { m.CloudProvider, Reason = m.DeadLetterReason ?? "Unknown" })
            .Select(g => new FleetTopFailure(g.Key.CloudProvider, g.Key.Reason, g.Count()))
            .OrderByDescending(f => f.Count).ThenBy(f => f.Provider).ThenBy(f => f.Reason, StringComparer.Ordinal)
            .Take(TopFailureRows).ToList();

        // Worst first: needs a look, then cannot tell, then healthy; deepest queue first within a word.
        var ordered = rows.OrderBy(r => r.Health switch { FleetHealth.NeedsALook => 0, FleetHealth.CannotTell => 1, _ => 2 })
            .ThenByDescending(r => r.Active ?? 0).ThenBy(r => r.Name, StringComparer.Ordinal).ToList();

        return new FleetOverview(window, since, clouds, ordered, topFailures);
    }
}
