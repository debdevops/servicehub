using System.Security.Cryptography;
using System.Text;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Infrastructure.AI;

/// <summary>
/// Builds stable, deterministic fingerprints from failure features.
/// Fingerprints are versioned to support algorithm evolution.
/// </summary>
public sealed class FailureFingerprintBuilder : IFailureFingerprintBuilder
{
    /// <summary>Current fingerprinting algorithm version.</summary>
    private const int FingerprintVersion = 1;

    /// <summary>Unit separator used in canonical fingerprint string.</summary>
    private const char CanonicalSeparator = '|';

    public int CurrentVersion => FingerprintVersion;

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
            Version = FingerprintVersion,
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

        // Add delivery count bucket (useful for aggregation)
        if (features.DeliveryCount > 0)
        {
            var bucket = features.DeliveryCount switch
            {
                <= 3 => "deliveries:low",
                <= 10 => "deliveries:medium",
                _ => "deliveries:high",
            };
            terms.Add(bucket);
        }

        return terms.Take(5).ToList(); // Limit to top 5 for stability
    }

    /// <summary>
    /// Build a canonical string representation for hashing.
    /// Deterministic: same features always produce the same string.
    /// Sortable: term order doesn't affect the result.
    /// </summary>
    private static string BuildCanonicalString(FailureFeatures features, IReadOnlyList<string> topTerms)
    {
        var parts = new List<string>
        {
            // Version prefix for forward compatibility
            $"v{FingerprintVersion}",

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
