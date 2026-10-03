namespace ServiceHub.Core.Models;

/// <summary>
/// Immutable fingerprint representing the stable identity of a failure class.
///
/// A fingerprint is computed deterministically from FailureFeatures and serves as
/// a universal identifier for a recurring failure pattern. The same failure class
/// always produces the same fingerprint, regardless of clustering strategy.
///
/// Fingerprints are versioned to support algorithm evolution: if the fingerprinting
/// algorithm changes in a future version, historical fingerprints remain valid and
/// can be compared against the current algorithm version.
/// </summary>
public sealed record FailureFingerprint
{
    /// <summary>Version of the fingerprinting algorithm used to compute this fingerprint.</summary>
    public int Version { get; init; }

    /// <summary>
    /// Stable SHA256 hex hash of the failure's distinguishing characteristics.
    /// Same input features always produce the same hash.
    /// Different fingerprint versions produce different hashes even from identical features.
    /// </summary>
    public required string Hash { get; init; }

    /// <summary>The features used to compute this fingerprint.</summary>
    public required FailureFeatures Features { get; init; }

    /// <summary>
    /// Confidence level of this fingerprint's classification [0.0 .. 1.0].
    /// Higher confidence indicates the fingerprint represents a stable, repeatable failure pattern.
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>Top distinguishing terms for this failure pattern.</summary>
    public IReadOnlyList<string> TopTerms { get; init; } = [];

    /// <summary>
    /// Gets a string representation of this fingerprint for logging and debugging.
    /// Format: "v{version}:{hash}"
    /// </summary>
    public override string ToString() => $"v{Version}:{Hash}";
}
