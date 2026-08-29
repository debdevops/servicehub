using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// One-shot startup seed for <see cref="GovernanceGrant"/> (M3 of the persistence wave) —
/// grandfathers every existing account into exactly the access it already had (persistence design
/// §5): one fleet-wide <see cref="GovernanceRole.Admin"/> grant per distinct <c>OwnerId</c> observed
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

    /// <summary>Idempotent: skips entirely if <c>GovernanceGrants</c> is already non-empty.</summary>
    public static async Task SeedIfEmptyAsync(DlqDbContext dbContext, ILogger logger)
    {
        if (await dbContext.GovernanceGrants.AnyAsync())
        {
            return;
        }

        var namespaceOwnerIds = await dbContext.Namespaces.Select(n => n.OwnerId).Distinct().ToListAsync();
        var ruleOwnerIds = await dbContext.AutoReplayRules.Select(r => r.OwnerId).Distinct().ToListAsync();
        var distinctOwnerIds = namespaceOwnerIds.Union(ruleOwnerIds, StringComparer.Ordinal).ToList();

        if (distinctOwnerIds.Count == 0)
        {
            // Nothing to grandfather — a genuinely fresh install with no namespaces or rules yet.
            return;
        }

        var now = DateTimeOffset.UtcNow;
        var adminGrants = distinctOwnerIds.Select(ownerId => new GovernanceGrant
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
        // B has Operator access to N."
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

        if (adminGrants.Count != distinctOwnerIds.Count)
        {
            logger.LogWarning(
                "Governance grant seed count mismatch: seeded {Seeded} Admin grant(s) for {Expected} distinct owner(s) — recoverable by hand.",
                adminGrants.Count, distinctOwnerIds.Count);
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
