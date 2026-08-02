using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Manages operational knowledge for recurring FailureSignatures.
///
/// The knowledge service enables engineers to capture and reuse institutional
/// memory: why failures happen, how they were fixed, who owns them, and
/// operational guidance.
///
/// This is the operational memory layer of ServiceHub.
/// </summary>
public interface IFailureKnowledgeService
{
    /// <summary>
    /// Retrieve knowledge for a specific failure signature.
    /// </summary>
    /// <param name="ownerId">Owner for multi-tenant isolation.</param>
    /// <param name="namespaceId">Namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's fingerprint hash.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// FailureKnowledge if it exists, or a default/empty knowledge object.
    /// Never returns null; always returns a valid FailureKnowledge.
    /// </returns>
    Task<Result<FailureKnowledge>> GetKnowledgeAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update or create knowledge for a signature.
    /// </summary>
    /// <param name="ownerId">Owner for multi-tenant isolation.</param>
    /// <param name="namespaceId">Namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's fingerprint hash.</param>
    /// <param name="knowledge">The knowledge to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The persisted knowledge with updated timestamps and version.
    /// </returns>
    Task<Result<FailureKnowledge>> UpsertKnowledgeAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        FailureKnowledge knowledge,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get knowledge for multiple signatures in batch.
    /// More efficient than calling GetKnowledgeAsync in a loop.
    /// </summary>
    /// <param name="ownerId">Owner for multi-tenant isolation.</param>
    /// <param name="namespaceId">Namespace the signatures belong to.</param>
    /// <param name="signatureHashes">The signature hashes to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Dictionary mapping signature hash → FailureKnowledge.
    /// Missing hashes get default empty knowledge objects.
    /// </returns>
    Task<Result<IReadOnlyDictionary<string, FailureKnowledge>>> GetKnowledgeBatchAsync(
        string ownerId,
        Guid namespaceId,
        IReadOnlyList<string> signatureHashes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark knowledge as needing review.
    /// Sets ReviewDueAt to the specified date.
    /// </summary>
    /// <param name="ownerId">Owner for multi-tenant isolation.</param>
    /// <param name="namespaceId">Namespace the signature belongs to.</param>
    /// <param name="signatureHash">The signature's fingerprint hash.</param>
    /// <param name="reviewDueAt">When to review this knowledge.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<Result<FailureKnowledge>> MarkForReviewAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        DateTimeOffset reviewDueAt,
        CancellationToken cancellationToken = default);
}
