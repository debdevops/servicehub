using System.Text;
using ServiceHub.Core.Interfaces;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Renders a DLQ error cluster's metadata plus its signature history into a single,
/// human-readable sentence — e.g. "4,412 messages (61% of the batch): max delivery count
/// exceeded on OrderService, first seen 18 minutes ago." No LLM, no generative model: the
/// cluster shape is fully known, so a template composes the sentence deterministically.
/// </summary>
internal static class ClusterExplanationRenderer
{
    /// <summary>
    /// A cluster's (or singleton's) reportable metadata, deliberately independent of the
    /// clustering.py dict shape — a caller adapts that response into this record.
    /// </summary>
    /// <param name="Size">Number of messages in the cluster (>= 1).</param>
    /// <param name="BatchSize">
    /// Total messages in the scan the cluster came from, for "share of the batch". Pass 0
    /// when unknown to omit the share clause.
    /// </param>
    /// <param name="DominantEntity">Most common queue/topic/subscription name, if any.</param>
    /// <param name="DominantDeadletterReason">Most common raw deadletter reason, if any.</param>
    /// <param name="DominantDeadletterReasonCount">
    /// How many of <paramref name="Size"/> members share <paramref name="DominantDeadletterReason"/>.
    /// Less than <paramref name="Size"/> means the cluster's failure reasons are mixed.
    /// </param>
    /// <param name="TopTerms">Top distinguishing terms for the cluster, if any.</param>
    internal sealed record ClusterMetadata(
        int Size,
        int BatchSize,
        string? DominantEntity,
        string? DominantDeadletterReason,
        int DominantDeadletterReasonCount,
        IReadOnlyList<string>? TopTerms = null);

    /// <summary>
    /// Composes the readable sentence for one cluster.
    /// </summary>
    /// <param name="cluster">The cluster's metadata.</param>
    /// <param name="signature">
    /// The cluster's signature history (new-vs-recurring), or <see langword="null"/> when no
    /// signature history is available for this cluster (e.g. an unclustered singleton).
    /// </param>
    /// <param name="now">The current time, for phrasing the time window relatively.</param>
    internal static string Render(ClusterMetadata cluster, SignatureLookupResult? signature, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(cluster);

        var isMixed = cluster.DominantDeadletterReasonCount < cluster.Size;
        var (reasonPhrase, showTerms) = DescribeReason(cluster.DominantDeadletterReason, isMixed);

        var sb = new StringBuilder();
        sb.Append(CountClause(cluster.Size, cluster.BatchSize));
        sb.Append(": ");
        sb.Append(reasonPhrase);
        sb.Append(EntityClause(cluster.DominantEntity));
        sb.Append(TermsClause(showTerms, cluster.TopTerms));

        var timeClause = TimeClause(signature, now);
        if (timeClause is not null)
        {
            sb.Append(", ");
            sb.Append(timeClause);
        }

        sb.Append('.');
        return sb.ToString();
    }

    private static string CountClause(int size, int batchSize)
    {
        var noun = size == 1 ? "1 message" : $"{size:N0} messages";
        if (batchSize <= 0)
            return noun;

        var pct = size * 100.0 / batchSize;
        var pctText = pct < 1 ? "<1%" : $"{Math.Round(pct):0}%";
        return $"{noun} ({pctText} of the batch)";
    }

    private static (string Phrase, bool ShowTerms) DescribeReason(string? reason, bool isMixed)
    {
        if (isMixed)
            return ("mixed failure reasons", true);

        if (string.IsNullOrWhiteSpace(reason))
            return ("unknown failure reason", true);

        // Azure Service Bus system reasons.
        if (Eq(reason, "MaxDeliveryCountExceeded"))
            return ("max delivery count exceeded", false);
        if (Eq(reason, "TTLExpiredException"))
            return ("message TTL expired", false);
        if (Eq(reason, "HeaderSizeExceeded"))
            return ("message header size limit exceeded", false);
        if (Eq(reason, "MessageSizeExceeded"))
            return ("message body size limit exceeded", false);
        if (Eq(reason, "SessionFilterMismatch"))
            return ("session filter mismatch", false);

        // AWS SQS.
        if (Eq(reason, "MaxReceiveCount"))
            return ("SQS max receive count exceeded", false);

        // GCP Pub/Sub.
        if (Eq(reason, "maxDeliveryAttempts"))
            return ("Pub/Sub max delivery attempts exceeded", false);
        if (Eq(reason, "nack"))
            return ("subscriber nack", false);

        // Explicit application-supplied reason — not one of the known system reasons above.
        return ($"application dead-letter: \"{reason}\"", true);
    }

    private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    private static string EntityClause(string? entity) =>
        string.IsNullOrWhiteSpace(entity) ? string.Empty : $" on {entity}";

    private static string TermsClause(bool show, IReadOnlyList<string>? terms)
    {
        if (!show || terms is null || terms.Count == 0)
            return string.Empty;

        return $" — distinguishing terms: {string.Join(", ", terms)}";
    }

    private static string? TimeClause(SignatureLookupResult? signature, DateTimeOffset now)
    {
        if (signature is null)
            return null;

        var elapsed = now - signature.FirstSeenAt;

        return signature.IsNew
            ? $"first seen {RelativeTime(elapsed)} ago"
            : $"recurring, seen {Pluralize(signature.OccurrenceCount, "time")} over {RelativeTime(elapsed)}";
    }

    private static string RelativeTime(TimeSpan elapsed)
    {
        if (elapsed < TimeSpan.Zero)
            elapsed = TimeSpan.Zero;

        if (elapsed < TimeSpan.FromMinutes(1))
            return "moments";
        if (elapsed < TimeSpan.FromHours(1))
            return Pluralize((int)elapsed.TotalMinutes, "minute");
        if (elapsed < TimeSpan.FromDays(1))
            return Pluralize((int)elapsed.TotalHours, "hour");
        if (elapsed < TimeSpan.FromDays(14))
            return Pluralize((int)elapsed.TotalDays, "day");
        if (elapsed < TimeSpan.FromDays(60))
            return Pluralize((int)(elapsed.TotalDays / 7), "week");
        if (elapsed < TimeSpan.FromDays(365))
            return Pluralize((int)(elapsed.TotalDays / 30), "month");

        return Pluralize((int)(elapsed.TotalDays / 365), "year");
    }

    private static string Pluralize(int n, string unit) => n == 1 ? $"1 {unit}" : $"{n} {unit}s";
}
