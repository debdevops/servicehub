using Microsoft.EntityFrameworkCore;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Helpers;
using ServiceHub.Infrastructure.Persistence;

namespace ServiceHub.Infrastructure.Signatures;

/// <summary>
/// Works out the capped error shape for one message (design 10 §5) from the dead letters already recorded for the same
/// namespace, reason and queue. Nothing is stored: the shapes in use are recomputed from the recorded error text, oldest first.
/// Only the cloud's own error description counts — never the message body, which is business data, not a reason (AWS and Google Cloud
/// record no error text, so every message there has the empty shape and nothing is split).
/// </summary>
/// <remarks>
/// Not used by anything yet — fingerprint v2 is not wired. Known limit: if the oldest messages of a queue are purged, the
/// "first seen" order is recomputed from what remains, so a purge can change which shapes hold a slot for <i>new</i> messages
/// (messages that already carry a signature keep it).
/// </remarks>
public static class ErrorTemplateCatalog
{
    /// <summary>How many recorded rows are read at most, so a huge queue cannot make this slow.</summary>
    private const int MaxRowsRead = 20_000;

    /// <summary>The shape <paramref name="message"/> gets under the cap: its own, or <see cref="ErrorTemplateCap.Other"/>.</summary>
    public static async Task<string> TemplateForAsync(ServiceHubDbContext db, DlqMessage message, int max, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(message);

        var own = ErrorTemplate.Normalize(message.DeadLetterErrorDescription);
        if (own.Length == 0)
        {
            return own;
        }

        var known = await KnownAsync(db, message, max, ct).ConfigureAwait(false);
        return ErrorTemplateCap.Apply(known, own, max);
    }

    /// <summary>The distinct shapes already in use for this message's namespace, reason and queue, oldest first, at most <paramref name="max"/>.</summary>
    public static async Task<IReadOnlyList<string>> KnownAsync(ServiceHubDbContext db, DlqMessage message, int max, CancellationToken ct)
    {
        var reason = message.DeadLetterReason;
        var rows = await db.DlqMessages.AsNoTracking()
            .Where(m => m.OwnerId == message.OwnerId && m.NamespaceId == message.NamespaceId
                && m.EntityName == message.EntityName && m.DeadLetterReason == reason && m.Id != message.Id)
            .OrderBy(m => m.DetectedAtUtc).ThenBy(m => m.Id)
            .Select(m => new { m.DeadLetterErrorDescription })
            .Take(MaxRowsRead)
            .ToListAsync(ct).ConfigureAwait(false);

        // Rows added earlier in the same scan are tracked but not saved yet; they count too.
        var pending = db.DlqMessages.Local
            .Where(m => m != message && m.OwnerId == message.OwnerId && m.NamespaceId == message.NamespaceId
                && m.EntityName == message.EntityName && m.DeadLetterReason == reason && m.Id == 0)
            .OrderBy(m => m.DetectedAtUtc)
            .Select(m => new { m.DeadLetterErrorDescription });

        var known = new List<string>();
        foreach (var row in rows.Concat(pending))
        {
            var t = ErrorTemplate.Normalize(row.DeadLetterErrorDescription);
            if (t.Length == 0 || t == ErrorTemplateCap.Other || known.Contains(t))
            {
                continue;
            }

            known.Add(t);
            if (known.Count >= max)
            {
                break;
            }
        }

        return known;
    }
}
