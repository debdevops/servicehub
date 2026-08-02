using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Recognizes and tracks recurring Failure Signatures.
///
/// When a new fingerprint is observed:
/// 1. Checks if it matches an existing signature (by fingerprint hash)
/// 2. If match: increments occurrence count, updates last-seen timestamp
/// 3. If no match: creates a new signature
///
/// Recognition is deterministic: same fingerprint always maps to the same signature.
///
/// This service bridges the gap between low-level fingerprinting and high-level
/// business concepts. Users should think "recurring failure" not "fingerprint".
/// </summary>
public interface IFailureSignatureRecognitionService
{
    /// <summary>
    /// Recognizes a single fingerprint and returns (or creates) its signature.
    /// </summary>
    /// <param name="ownerId">Owner for multi-tenant isolation.</param>
    /// <param name="namespaceId">Namespace to recognize signature in.</param>
    /// <param name="fingerprint">The fingerprint to recognize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The recognized (existing) or newly created FailureSignature.
    /// Deterministic: calling twice with the same fingerprint returns the same signature.
    /// </returns>
    Task<Result<FailureSignature>> RecognizeAsync(
        string ownerId,
        Guid namespaceId,
        FailureFingerprint fingerprint,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Batch recognize multiple fingerprints in a single operation.
    /// More efficient than calling RecognizeAsync in a loop.
    /// </summary>
    /// <param name="ownerId">Owner for multi-tenant isolation.</param>
    /// <param name="namespaceId">Namespace to recognize signatures in.</param>
    /// <param name="fingerprints">The fingerprints to recognize.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of FailureSignatures, one per fingerprint in order.</returns>
    Task<Result<IReadOnlyList<FailureSignature>>> RecognizeBatchAsync(
        string ownerId,
        Guid namespaceId,
        IReadOnlyList<FailureFingerprint> fingerprints,
        CancellationToken cancellationToken = default);
}
