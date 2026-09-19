using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Startup seed for <see cref="GovernanceGrant"/> (M3 of the persistence wave) — grandfathers
/// every existing account into exactly the access it already had (persistence design §5): one
/// fleet-wide <see cref="GovernanceRole.Admin"/> grant per distinct <c>OwnerId</c> observed
/// across owner-scoped tables, plus one namespace-scoped <see cref="GovernanceRole.Operator"/> grant
/// per <see cref="NamespaceSharedOwner"/> row (M2) — "was shared with" today implies full
/// functional access, not read-only, so <c>Operator</c> is the accurate translation, not <c>Viewer</c>.
/// <para>
/// Unlike <see cref="NamespaceStoreImporter"/>'s row-count gate, a mismatch here is only logged as
/// a warning, never gating: grant seeding is recoverable by hand post-migration in a way the
/// namespace import is not (persistence design §10).
/// </para>
/// </summary>
public static class GovernanceGrantSeeder
{
    private const string SeedActorIdentity = "System:GovernanceGrantSeed";

    /// <summary>
    /// Idempotent <em>per owner</em>, not per table: an owner with at least one existing
    /// <see cref="GovernanceGrant"/> row (of any role, for or against any grantee) is left alone —
    /// re-seeding it could silently reopen a deliberate lockdown. An owner with zero grant rows
    /// gets grandfathered.
    /// <para>
    /// This must run every startup, not only once — a fresh <c>OwnerId</c> partition can appear
    /// at any time (e.g. a new scoped API key with <c>namespaces:write</c> registers its own
    /// namespaces well after the very first owner was seeded). Before this method ran on every
    /// distinct owner rather than only when the whole table was still empty, that later owner's
    /// <see cref="ServiceHub.Infrastructure.Governance.GovernanceAccessEvaluator"/> bootstrap
    /// bypass ("no active grants yet for this owner → unrestricted") closed the instant its first
    /// grant was created — with no Admin grant ever seeded to replace it, and no way back in
    /// through the API, since granting itself requires Admin. (Live-found 2026-09-19: a freshly
    /// registered owner's own admin-scoped API key locked itself out on the very next Governance
    /// call after creating one unrelated Viewer grant.)
    /// </para>
    /// </summary>
    public static async Task SeedIfEmptyAsync(DlqDbContext dbContext, ILogger logger)
    {
        var namespaceOwnerIds = await dbContext.Namespaces.Select(n => n.OwnerId).Distinct().ToListAsync();
        var ruleOwnerIds = await dbContext.AutoReplayRules.Select(r => r.OwnerId).Distinct().ToListAsync();
        var distinctOwnerIds = namespaceOwnerIds.Union(ruleOwnerIds, StringComparer.Ordinal).ToList();

        if (distinctOwnerIds.Count == 0)
        {
            // Nothing to grandfather — a genuinely fresh install with no namespaces or rules yet.
            return;
        }

        var ownersWithExistingGrants = (await dbContext.GovernanceGrants
                .Select(g => g.OwnerId)
                .Distinct()
                .ToListAsync())
            .ToHashSet(StringComparer.Ordinal);

        var ownersNeedingSeed = distinctOwnerIds.Where(id => !ownersWithExistingGrants.Contains(id)).ToList();
        if (ownersNeedingSeed.Count == 0)
        {
            // Every owner that currently exists already has some Governance state of its own —
            // nothing new to grandfather.
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var adminGrants = ownersNeedingSeed.Select(ownerId => new GovernanceGrant
        {
            OwnerId = ownerId,
            GranteeIdentity = ownerId,
            GranteeKind = ClassifyGranteeKind(ownerId),
            Role = GovernanceRole.Admin,
            NamespaceId = null,
            PillarKind = null,
            GrantedAt = now,
            GrantedByIdentity = SeedActorIdentity,
        }).ToList();

        dbContext.GovernanceGrants.AddRange(adminGrants);

        // One Operator grant per NamespaceSharedOwners row, scoped to the sharing namespace's own
        // owner partition — "owner A shared namespace N with owner B" becomes "A's grant list says
        // B has Operator access to N." Only for owners newly seeded above: an already-governed
        // owner's share grants are that owner's own business to manage from here on.
        var ownersNeedingSeedSet = ownersNeedingSeed.ToHashSet(StringComparer.Ordinal);
        var shares = await dbContext.NamespaceSharedOwners.ToListAsync();
        var namespaceOwnerById = await dbContext.Namespaces
            .Select(n => new { n.Id, n.OwnerId })
            .ToDictionaryAsync(n => n.Id, n => n.OwnerId);

        var operatorGrants = new List<GovernanceGrant>();
        foreach (var share in shares)
        {
            if (!namespaceOwnerById.TryGetValue(share.NamespaceId, out var namespaceOwnerId))
            {
                continue;
            }

            if (!ownersNeedingSeedSet.Contains(namespaceOwnerId))
            {
                continue;
            }

            operatorGrants.Add(new GovernanceGrant
            {
                OwnerId = namespaceOwnerId,
                GranteeIdentity = share.OwnerId,
                GranteeKind = ClassifyGranteeKind(share.OwnerId),
                Role = GovernanceRole.Operator,
                NamespaceId = share.NamespaceId,
                PillarKind = null,
                GrantedAt = now,
                GrantedByIdentity = SeedActorIdentity,
            });
        }

        dbContext.GovernanceGrants.AddRange(operatorGrants);

        await dbContext.SaveChangesAsync();

        if (adminGrants.Count != ownersNeedingSeed.Count)
        {
            logger.LogWarning(
                "Governance grant seed count mismatch: seeded {Seeded} Admin grant(s) for {Expected} distinct owner(s) needing seed — recoverable by hand.",
                adminGrants.Count, ownersNeedingSeed.Count);
        }

        logger.LogInformation(
            "Governance grant seed complete: {AdminCount} fleet-wide Admin grant(s), {OperatorCount} namespace-scoped Operator grant(s)",
            adminGrants.Count, operatorGrants.Count);
    }

    /// <summary>
    /// OwnerId format convention (see <c>AutoReplayRule.OwnerId</c>'s own doc comment):
    /// <c>entra:{oid}</c>/<c>__spa__</c> are human/admin sessions, <c>key_{hash}</c> is a scoped API key.
    /// </summary>
    private static GranteeKind ClassifyGranteeKind(string ownerId) =>
        ownerId.StartsWith("key_", StringComparison.Ordinal) ? GranteeKind.ApiKey : GranteeKind.User;
}
