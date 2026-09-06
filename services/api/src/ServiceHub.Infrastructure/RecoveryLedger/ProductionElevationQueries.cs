using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// The one query every front-door Prod recovery guard shares to answer "is there a live
/// elevation covering this namespace right now" (ADR-0010 §Decision phase 2) — used both by
/// <see cref="RecoveryLedgerService.GetLiveProductionElevationAsync"/> (the Eligibility Gate's
/// predicate 2 read) and by the pre-flight validation guards in <c>BulkOperationService</c> and
/// <c>SignatureReplayService</c>, which have a <see cref="DlqDbContext"/> but not a full
/// <c>IRecoveryLedger</c> dependency. Kept as one static query rather than duplicated inline so
/// "live" means exactly one thing everywhere it is checked.
/// </summary>
internal static class ProductionElevationQueries
{
    public static async Task<ProductionElevation?> GetLiveAsync(
        DlqDbContext dbContext, string ownerId, Guid namespaceId, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await dbContext.ProductionElevations
            .AsNoTracking()
            .Where(e => e.OwnerId == ownerId && e.NamespaceId == namespaceId
                && e.ApprovedAt != null && e.RevokedAt == null)
            .OrderByDescending(e => e.ApprovedAt)
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(e => e.IsLiveAt(now));
    }
}
