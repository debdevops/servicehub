using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Builds stable, deterministic fingerprints from failure characteristics.
///
/// A fingerprint is the canonical identity of a failure class. The same features
/// always produce the same fingerprint, enabling signature tracking across time
/// and clustering strategy changes.
///
/// Fingerprint building is deterministic and strategy-independent.
/// Strategies never build fingerprints — they only cluster them.
/// </summary>
public interface IFailureFingerprintBuilder
{
    /// <summary>
    /// Compute a stable fingerprint from extracted features.
    /// </summary>
    /// <param name="features">The failure characteristics to fingerprint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Immutable FailureFingerprint with stable hash and metadata.</returns>
    Task<Result<FailureFingerprint>> ComputeAsync(
        FailureFeatures features,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch fingerprint computation for better performance.
    /// </summary>
    /// <param name="featuresList">Extracted features to fingerprint.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of FailureFingerprints, one per features in order.</returns>
    Task<Result<IReadOnlyList<FailureFingerprint>>> ComputeBatchAsync(
        IReadOnlyList<FailureFeatures> featuresList,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current fingerprinting algorithm version.
    /// This enables forward-compatible verification of historical fingerprints.
    /// </summary>
    int CurrentVersion { get; }
}
