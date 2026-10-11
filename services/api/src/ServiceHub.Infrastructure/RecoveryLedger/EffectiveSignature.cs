using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.RecoveryLedger;

/// <summary>
/// Which signature a ledger entry counts under (design 10 §6, option R2 — re-attribute).
/// </summary>
/// <remarks>
/// <para>
/// A ledger entry records the signature its message had when it was replayed (<c>SignatureHashSnapshot</c>). The ledger is
/// append-only, so that snapshot never changes — but after the re-sign (<c>SignatureResigner</c>) the message carries a
/// <i>new</i> signature, and the old one names nothing. Counting by the snapshot alone would leave the new, split signatures
/// with no history at all and keep crediting dead hashes.
/// </para>
/// <para>
/// The <b>effective</b> signature of an entry is the one its message carries now; when the message is gone (purged), or was never
/// signed, it is the snapshot. So every replay is counted under the cause it actually belonged to and under nothing else, and
/// before any re-sign (message hash equals snapshot) this changes no answer. Entries whose message is gone keep the old hash,
/// which no live message carries, so they stop counting for anyone: that is the "reset" fallback (R1) from the design.
/// </para>
/// </remarks>
public static class EffectiveSignature
{
    /// <summary>The owner's entries whose effective signature is <paramref name="signatureHash"/>.</summary>
    public static IQueryable<RecoveryLedgerEntry> ForSignature(
        this IQueryable<RecoveryLedgerEntry> entries, ServiceHubDbContext db, string ownerId, string signatureHash) =>
        entries.Where(e => e.OwnerId == ownerId
            && (db.DlqMessages.Any(m => m.Id == e.DlqMessageId && m.SignatureHash == signatureHash)
                || (e.SignatureHashSnapshot == signatureHash
                    && !db.DlqMessages.Any(m => m.Id == e.DlqMessageId && m.SignatureHash != null && m.SignatureHash != signatureHash))));
}
