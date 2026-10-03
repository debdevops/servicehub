using ServiceHub.Core.Entities;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Strategy interface for analyzing DLQ messages and producing error clusters.
/// Implementations can use different clustering methods (AI-based, deterministic, heuristic, etc.)
/// but all must return the same <see cref="ClusterAnalysisResult"/> contract.
///
/// This abstraction enables graceful fallback: when one strategy fails or is unavailable,
/// another can provide results without changing consumer code.
/// </summary>
public interface ISignatureAnalysisStrategy
{
    /// <summary>
    /// Analyzes a batch of DLQ messages and groups them into error clusters.
    /// </summary>
    /// <param name="messages">The DLQ messages to cluster.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A result containing clusters, singletons, method name, and the ref→messageId mapping.
    /// The method name identifies which strategy produced the result (for observability/logging,
    /// not for UI display).
    /// </returns>
    Task<Result<ClusterAnalysisResult>> AnalyzeAsync(
        IReadOnlyList<DlqMessage> messages,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyzes pre-built failure fingerprints and groups them into error clusters.
    /// This is the fingerprint-based path (preferred for new code).
    /// </summary>
    /// <param name="fingerprints">Pre-extracted and fingerprinted failures.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A result containing clusters, singletons, and method name.
    /// </returns>
    Task<Result<ClusterAnalysisResult>> AnalyzeFingerprintsAsync(
        IReadOnlyList<FailureFingerprint> fingerprints,
        CancellationToken cancellationToken = default);
}
