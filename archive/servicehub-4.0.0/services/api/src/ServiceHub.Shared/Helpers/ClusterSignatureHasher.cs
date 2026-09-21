using System.Security.Cryptography;
using System.Text;

namespace ServiceHub.Shared.Helpers;

/// <summary>
/// Computes a stable identity hash for a DLQ error cluster from its top distinguishing
/// terms and dominant deadletter reason — deliberately excludes cluster size, member refs,
/// and timestamps, all of which change between scans of the same underlying failure.
/// </summary>
public static class ClusterSignatureHasher
{
    // Unit separator (0x1F) — won't appear in TF-IDF term text or a deadletter reason string.
    private const char TermDelimiter = '';

    /// <summary>
    /// Computes the signature hash. Terms are trimmed, lowercased, and sorted ordinally
    /// before hashing so that a reordering of near-tied TF-IDF scores between scans of the
    /// same failure does not change the hash.
    /// </summary>
    public static string ComputeHash(IReadOnlyList<string> topTerms, string dominantDeadletterReason)
    {
        ArgumentNullException.ThrowIfNull(topTerms);
        ArgumentNullException.ThrowIfNull(dominantDeadletterReason);

        var normalisedTerms = topTerms
            .Select(t => t.Trim().ToLowerInvariant())
            .OrderBy(t => t, StringComparer.Ordinal)
            .ToArray();

        var canonical = string.Join(TermDelimiter, normalisedTerms) + TermDelimiter + dominantDeadletterReason.Trim();
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);

        return Convert.ToHexStringLower(hash);
    }
}
