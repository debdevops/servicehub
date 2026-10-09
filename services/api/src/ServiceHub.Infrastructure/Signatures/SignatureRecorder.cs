using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Helpers;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Signatures;

/// <summary>
/// Gives a newly seen dead letter its failure signature and keeps the per-namespace tally (unit 3.1).
/// </summary>
/// <remarks>
/// It changes nothing in the cloud and decides nothing: it reads a message ServiceHub already recorded, computes the
/// fingerprint with the copied builder, and counts it. Same failure → same hash → one row; a different failure → a
/// different hash → another row.
/// </remarks>
public sealed class SignatureRecorder
{
    private static readonly FailureFeatureExtractor Extractor = new();
    private static readonly FailureFingerprintBuilder BuilderV1 = new();
    private static readonly FailureFingerprintBuilder BuilderV2 = new(includeErrorTemplate: true);

    /// <summary>
    /// Computes <paramref name="message"/>'s fingerprint, adds or updates its <see cref="NamespaceSignature"/> row in
    /// <paramref name="db"/> (the caller saves) and returns the hash.
    /// </summary>
    public static async Task<string> AssignAsync(ServiceHubDbContext db, DlqMessage message, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(message);

        var features = (await Extractor.ExtractAsync(message, ct).ConfigureAwait(false)).Value;
        var template = await ErrorTemplateCatalog.TemplateForAsync(db, message, ErrorTemplateCap.DefaultMax, ct).ConfigureAwait(false);
        var fingerprint = (await BuilderV2.ComputeAsync(features with { ErrorTemplate = template }, ct).ConfigureAwait(false)).Value;
        var hash = fingerprint.Hash;

        // A signature made before failures were grouped by error message, and never split by a re-sign, keeps its identity (and the
        // trust, grant and rules that go with it). A new message with the same shape joins it instead of starting a second signature.
        var legacy = (await BuilderV1.ComputeAsync(features, ct).ConfigureAwait(false)).Value.Hash;
        if (legacy != hash)
        {
            var old = db.NamespaceSignatures.Local.FirstOrDefault(s =>
                    s.OwnerId == message.OwnerId && s.NamespaceId == message.NamespaceId && s.SignatureHash == legacy)
                ?? await db.NamespaceSignatures.AsNoTracking().FirstOrDefaultAsync(s =>
                    s.OwnerId == message.OwnerId && s.NamespaceId == message.NamespaceId && s.SignatureHash == legacy, ct).ConfigureAwait(false);
            if (old is not null && ErrorTemplate.Normalize(old.ExampleError) == template)
            {
                hash = legacy;
            }
            else if (old is null)
            {
                // Not in this namespace. Auto-replay rules belong to the owner and cloud and are bound to the (namespace-independent) v1 hash, so a message
                // in a newer namespace — a second account, or a cloud removed and added again — must also join an older unsplit signature of the same
                // owner, or every rule made before the v2 switch silently stops matching it. Only when this namespace has not already started its own v2 row.
                var startedHere = db.NamespaceSignatures.Local.Any(s => s.OwnerId == message.OwnerId && s.NamespaceId == message.NamespaceId && s.SignatureHash == hash)
                    || await db.NamespaceSignatures.AsNoTracking().AnyAsync(s => s.OwnerId == message.OwnerId && s.NamespaceId == message.NamespaceId && s.SignatureHash == hash, ct).ConfigureAwait(false);
                if (!startedHere)
                {
                    var elsewhere = db.NamespaceSignatures.Local.Where(s => s.OwnerId == message.OwnerId && s.SignatureHash == legacy).ToList();
                    if (elsewhere.Count == 0)
                    {
                        elsewhere = await db.NamespaceSignatures.AsNoTracking().Where(s => s.OwnerId == message.OwnerId && s.SignatureHash == legacy).ToListAsync(ct).ConfigureAwait(false);
                    }

                    if (elsewhere.Any(s => ErrorTemplate.Normalize(s.ExampleError) == template))
                    {
                        hash = legacy;
                    }
                }
            }
        }

        // Rows added earlier in this same scan are tracked but not yet saved, so look there first.
        var row = db.NamespaceSignatures.Local.FirstOrDefault(s =>
                s.OwnerId == message.OwnerId && s.NamespaceId == message.NamespaceId && s.SignatureHash == hash)
            ?? await db.NamespaceSignatures.FirstOrDefaultAsync(s =>
                s.OwnerId == message.OwnerId && s.NamespaceId == message.NamespaceId && s.SignatureHash == hash, ct).ConfigureAwait(false);

        if (row is null)
        {
            db.NamespaceSignatures.Add(new NamespaceSignature
            {
                NamespaceId = message.NamespaceId,
                OwnerId = message.OwnerId,
                SignatureHash = hash,
                FirstSeenAt = message.DetectedAtUtc,
                LastSeenAt = message.DetectedAtUtc,
                OccurrenceCount = 1,
                DominantDeadletterReason = message.DeadLetterReason ?? "Unknown",
                EntityName = message.EntityName,
                ExampleError = message.DeadLetterErrorDescription is { Length: > 1024 } err ? err[..1024] : message.DeadLetterErrorDescription,
                TopTermsJson = JsonSerializer.Serialize(fingerprint.TopTerms),
            });
        }
        else
        {
            row.OccurrenceCount++;
            if (message.DetectedAtUtc > row.LastSeenAt)
            {
                row.LastSeenAt = message.DetectedAtUtc;
            }
        }

        return hash;
    }

    /// <summary>
    /// Signs rows recorded before signatures existed, a few hundred a call, until none are left. Idempotent: a signed row is
    /// never touched again, so a restart in the middle loses nothing.
    /// </summary>
    public static async Task<int> BackfillAsync(ServiceHubDbContext db, Guid namespaceId, CancellationToken ct)
    {
        var unsigned = await db.DlqMessages.Where(m => m.NamespaceId == namespaceId && m.SignatureHash == null)
            .OrderBy(m => m.Id).Take(500).ToListAsync(ct).ConfigureAwait(false);
        foreach (var m in unsigned)
        {
            m.SignatureHash = await AssignAsync(db, m, ct).ConfigureAwait(false);
        }

        if (unsigned.Count > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return unsigned.Count;
    }
}
