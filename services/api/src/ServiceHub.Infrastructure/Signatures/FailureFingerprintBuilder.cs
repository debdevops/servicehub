using System.Security.Cryptography;
using System.Text;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Core.Helpers;
using ServiceHub.Core.Results;

namespace ServiceHub.Infrastructure.Signatures;

/// <summary>
/// Builds stable, deterministic fingerprints from failure features.
/// Fingerprints are versioned to support algorithm evolution.
/// </summary>
public sealed class FailureFingerprintBuilder : IFailureFingerprintBuilder
{
    /// <summary>Fingerprinting algorithm version 1: the one every existing signature was made with.</summary>
    private const int FingerprintVersion = 1;

    /// <summary>Version 2 adds the error message's shape to the hash (design 10 §5). Off unless asked for; nothing asks yet.</summary>
    private const int FingerprintVersionWithErrorTemplate = 2;

    /// <summary>Unit separator used in canonical fingerprint string.</summary>
    private const char CanonicalSeparator = '|';

    private readonly bool _includeErrorTemplate;

    /// <summary>Builds version-1 fingerprints — byte-for-byte what 4.1.0 produces.</summary>
    public FailureFingerprintBuilder() : this(includeErrorTemplate: false)
    {
    }

    /// <summary>
    /// <paramref name="includeErrorTemplate"/> <c>true</c> builds version-2 fingerprints, which also hash
    /// the failure's <see cref="FailureFeatures.ErrorTemplate"/> so two failures with different messages stop sharing a signature.
    /// Every v2 hash differs from its v1 hash, so switching it on changes the identity of every signature; it is not wired to
    /// anything and must not be until the owner has answered the open questions in design 10 §11.
    /// </summary>
    public FailureFingerprintBuilder(bool includeErrorTemplate)
    {
        _includeErrorTemplate = includeErrorTemplate;
    }

    private int Version => _includeErrorTemplate ? FingerprintVersionWithErrorTemplate : FingerprintVersion;

    public int CurrentVersion => Version;

    public async Task<Result<FailureFingerprint>> ComputeAsync(
        FailureFeatures features,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(features);

        var fingerprint = ComputeFingerprint(features);
        return await Task.FromResult(Result.Success(fingerprint)).ConfigureAwait(false);
    }

    public async Task<Result<IReadOnlyList<FailureFingerprint>>> ComputeBatchAsync(
        IReadOnlyList<FailureFeatures> featuresList,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(featuresList);

        var fingerprints = featuresList.Select(ComputeFingerprint).ToList();
        return await Task.FromResult(Result.Success((IReadOnlyList<FailureFingerprint>)fingerprints))
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Compute a fingerprint from features.
    /// Algorithm: deterministic hash of normalized distinguishing characteristics.
    /// </summary>
    private FailureFingerprint ComputeFingerprint(FailureFeatures features)
    {
        var topTerms = ExtractTopTerms(features);
        var canonical = BuildCanonicalString(features, topTerms);
        var hash = ComputeHash(canonical);
        var confidence = ComputeConfidence(features);

        return new FailureFingerprint
        {
            Version = Version,
            Hash = hash,
            Features = features,
            Confidence = confidence,
            TopTerms = topTerms,
        };
    }

    /// <summary>
    /// Extract distinguishing terms for this failure pattern.
    /// </summary>
    private static IReadOnlyList<string> ExtractTopTerms(FailureFeatures features)
    {
        var terms = new List<string>();

        // Add primary reason and entity
        if (!string.IsNullOrWhiteSpace(features.DeadLetterReason))
        {
            terms.Add($"reason:{features.DeadLetterReason}");
        }

        if (!string.IsNullOrWhiteSpace(features.EntityName))
        {
            terms.Add($"entity:{features.EntityName}");
        }

        // Add exception type if extracted
        if (!string.IsNullOrWhiteSpace(features.ExceptionType))
        {
            terms.Add($"exception:{features.ExceptionType}");
        }

        // Add provider
        terms.Add($"provider:{features.Provider}");

        // Add category if available
        if (!string.IsNullOrWhiteSpace(features.FailureCategory))
        {
            terms.Add($"category:{features.FailureCategory}");
        }

        // Add delivery count band. Must stay a band, never the raw count — see
        // DeliveryCountBucket's remarks. Changing this changes every existing fingerprint,
        // and AutonomyGrants are keyed by fingerprint hash.
        var deliveryBand = DeliveryCountBucket.Classify(features.DeliveryCount);
        if (deliveryBand is not null)
        {
            terms.Add($"deliveries:{deliveryBand}");
        }

        return terms.Take(5).ToList(); // Limit to top 5 for stability
    }

    /// <summary>
    /// Build a canonical string representation for hashing.
    /// Deterministic: same features always produce the same string.
    /// Sortable: term order doesn't affect the result.
    /// </summary>
    private string BuildCanonicalString(FailureFeatures features, IReadOnlyList<string> topTerms)
    {
        var parts = new List<string>
        {
            // Version prefix for forward compatibility
            $"v{Version}",

            // Core identifying fields (must be normalized)
            NormalizeForHash(features.DeadLetterReason),
            NormalizeForHash(features.EntityName),
            features.Provider.ToString().ToLowerInvariant(),

            // Additional distinguishing characteristics
            NormalizeForHash(features.FailureCategory ?? ""),
            NormalizeForHash(features.ExceptionType ?? ""),

            // Sorted terms for stability
            string.Join(",", topTerms.OrderBy(t => t, StringComparer.Ordinal)),
        };

        if (_includeErrorTemplate)
        {
            // Appended last, so a v1 canonical string is exactly the v2 one without this part.
            // Only a template the caller derived from the cloud's error description counts: ErrorTextNormalized falls back to the message
            // body, which is business data and would split one failure by customer or order.
            var template = features.ErrorTemplate ?? string.Empty;
            parts.Add(template.Length == 0 ? "null" : template);
        }

        return string.Join(CanonicalSeparator, parts);
    }

    /// <summary>
    /// Compute stable SHA256 hash of the canonical string.
    /// </summary>
    private static string ComputeHash(string canonical)
    {
        var bytes = Encoding.UTF8.GetBytes(canonical);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Normalize a string for consistent hashing.
    /// </summary>
    private static string NormalizeForHash(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "null";
        }

        return value.Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Compute confidence level for this fingerprint.
    /// Confidence increases with stability indicators (many delivery attempts,
    /// consistent entity, recognizable exception type, etc).
    /// </summary>
    private static double ComputeConfidence(FailureFeatures features)
    {
        var confidence = 0.5; // Base confidence

        // Increase confidence for known exception types
        if (!string.IsNullOrWhiteSpace(features.ExceptionType))
        {
            confidence += 0.1;
        }

        // Increase confidence for multiple delivery attempts
        if (features.DeliveryCount > 3)
        {
            confidence += 0.15;
        }

        // Increase confidence for known failure categories
        if (!string.IsNullOrWhiteSpace(features.FailureCategory))
        {
            confidence += 0.1;
        }

        // Increase confidence if message has user properties (more context)
        if (features.PropertyCount > 0)
        {
            confidence += 0.05;
        }

        return Math.Min(confidence, 1.0);
    }
}
