using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.DlqObserver;

/// <summary>
/// Which namespaces can prove their dead-letter queue stayed empty <i>because their observer is live</i>, whatever their
/// provider's own capability says. One read for many namespaces, for screens that show a verdict for a whole page of replays.
/// </summary>
/// <remarks>
/// Uses <see cref="DlqObserverAttestation.IsLiveAt"/>, the single fact every other consumer (the eligibility gate, the
/// verification agent, the autonomy evaluator) uses, so the screens cannot disagree with them: no row, an observer switched
/// off, or one that has not confirmed within its staleness bound is not live.
/// </remarks>
internal static class ObserverLiveness
{
    /// <summary>The ids, among <paramref name="namespaces"/>, whose observer is live now.</summary>
    internal static async Task<HashSet<Guid>> LiveAmongAsync(
        ServiceHubDbContext db, IReadOnlyCollection<Namespace> namespaces, CancellationToken cancellationToken)
    {
        var ids = namespaces.Select(n => n.Id).ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var attestations = await db.DlqObserverAttestations.AsNoTracking()
            .Where(a => ids.Contains(a.NamespaceId)).ToListAsync(cancellationToken).ConfigureAwait(false);
        var owners = namespaces.ToDictionary(n => n.Id, n => n.OwnerId);
        var now = DateTimeOffset.UtcNow;
        return attestations
            .Where(a => owners.TryGetValue(a.NamespaceId, out var owner) && owner == a.OwnerId && a.IsLiveAt(now))
            .Select(a => a.NamespaceId)
            .ToHashSet();
    }
}
