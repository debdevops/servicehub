using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Enums;
using ServiceHub.Shared.Helpers;

namespace ServiceHub.Infrastructure.Persistence;

/// <summary>
/// Classifies <c>NamespaceSignatures.HashKind</c> for rows the M1.4 migration
/// (<c>AddNextChapterM1PillarFindingsAndHashKind</c>, ADR-0009 §Decision unit 2) defaulted to
/// <see cref="SignatureHashKind.Fingerprint"/> — the roadmap's own stated default for a hash that
/// can't yet be classified. Invoked once from <c>Program.cs</c>, immediately after
/// <c>Database.MigrateAsync()</c>, the same slot <see cref="NamespaceStoreImporter"/> runs in.
/// </summary>
/// <remarks>
/// <para>
/// Re-derives <c>ClusterSignatureHasher.ComputeHash(topTerms, dominantDeadletterReason)</c> from
/// each row's own stored <c>TopTermsJson</c>/<c>DominantDeadletterReason</c> and compares it to
/// the stored <c>SignatureHash</c>. A match means the row was written by the cluster-hash path
/// (<c>DlqSignatureAnalysisService</c>) and is reclassified <see cref="SignatureHashKind.Cluster"/>.
/// Everything else — a genuine fingerprint row, or (per the roadmap) a row matching neither
/// vocabulary — stays <see cref="SignatureHashKind.Fingerprint"/>, logged at warning only in the
/// second case, and is never dropped.
/// </para>
/// <para>
/// <b>Never touches <c>SignatureHash</c> itself</b> — only the new <c>HashKind</c> column.
/// Changing fingerprint identity would orphan every earned <c>AutonomyGrant</c>, which is exactly
/// the safety boundary ADR-0009 states this migration must not cross.
/// </para>
/// <para>
/// Unlike <see cref="NamespaceStoreImporter"/>, this is not a one-shot forward-only cutover
/// gated on "have I already run" — it re-scans every row still marked <c>Fingerprint</c> on
/// every startup. That is deliberately safe to repeat: a row that is genuinely Fingerprint will
/// never reproduce a <c>ClusterSignatureHasher</c> match (the two vocabularies' canonical forms
/// use disjoint term prefixes — <c>category:</c>/<c>deliveries:</c> versus
/// <c>cause:</c>/<c>deliveryAttempts:</c>), so re-running this scan against an already-correct
/// table is a no-op. Every row written after this migration ships already carries its real,
/// explicitly-stated kind (see <c>NamespaceSignatureLookupService.LookupAndRecordAsync</c>'s
/// <c>hashKind</c> parameter) and is excluded from this scan by construction — it was never
/// defaulted to Fingerprint by the column default in the first place.
/// </para>
/// </remarks>
public static class NamespaceSignatureHashKindBackfiller
{
    /// <summary>
    /// Scans every <see cref="SignatureHashKind.Fingerprint"/>-classified row and reclassifies
    /// the ones whose stored terms/reason actually reproduce a
    /// <c>ClusterSignatureHasher.ComputeHash</c> match to <see cref="SignatureHashKind.Cluster"/>.
    /// Never throws — a failed backfill leaves rows over-classified as Fingerprint (the safe
    /// default), which degrades a filtered read to "misses this row" rather than corrupting
    /// anything, so it is intentionally non-fatal to startup, matching
    /// <c>GovernanceGrantSeeder.SeedIfEmptyAsync</c>'s own tolerance for this class of failure.
    /// </summary>
    public static async Task BackfillAsync(DlqDbContext dbContext, ILogger logger, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(dbContext);
        ArgumentNullException.ThrowIfNull(logger);

        try
        {
            var candidates = await dbContext.NamespaceSignatures
                .Where(s => s.HashKind == SignatureHashKind.Fingerprint)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (candidates.Count == 0)
            {
                return;
            }

            var reclassified = 0;
            var unresolved = 0;

            foreach (var signature in candidates)
            {
                List<string>? topTerms;
                try
                {
                    topTerms = JsonSerializer.Deserialize<List<string>>(signature.TopTermsJson);
                }
                catch (JsonException)
                {
                    topTerms = null;
                }

                if (topTerms is null)
                {
                    unresolved++;
                    continue;
                }

                var reproducedClusterHash = ClusterSignatureHasher.ComputeHash(topTerms, signature.DominantDeadletterReason);
                if (string.Equals(reproducedClusterHash, signature.SignatureHash, StringComparison.Ordinal))
                {
                    dbContext.Entry(signature).Property(nameof(Core.Entities.NamespaceSignature.HashKind)).CurrentValue =
                        SignatureHashKind.Cluster;
                    reclassified++;
                }
                // Neither reproduces a match nor is malformed: stays Fingerprint, the documented
                // default for "matches neither" (roadmap M1.4) — not logged individually, since a
                // healthy table has many of these (genuine fingerprint rows) and only the
                // JSON-malformed case below is actionable.
            }

            if (reclassified > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "NamespaceSignature HashKind backfill: reclassified {ReclassifiedCount} row(s) to Cluster out of {CandidateCount} still-Fingerprint row(s) scanned",
                    reclassified, candidates.Count);
            }

            if (unresolved > 0)
            {
                logger.LogWarning(
                    "NamespaceSignature HashKind backfill: {UnresolvedCount} row(s) had unparseable TopTermsJson and could not be classified — left as Fingerprint (the safe default), SignatureHash unchanged",
                    unresolved);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "NamespaceSignature HashKind backfill failed — rows remain classified Fingerprint (the safe default) until the next successful run");
        }
    }
}
