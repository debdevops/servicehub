using ServiceHub.Core.Enums;
using ServiceHub.Core.Models;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Manages the manual triage lifecycle (Active/Resolved/Reopened/Suppressed/Archived) of
/// failure signatures, keyed by (owner, namespace, signature hash) — the same natural key
/// used by <see cref="INamespaceSignatureLookupService"/> and <c>IFailureKnowledgeService</c>.
/// <para>
/// The current implementation holds this state in process memory only; it is not durable
/// across API restarts. This interface is the sole integration point for every caller, so a
/// future durable (EF Core-backed) implementation is a drop-in replacement with no caller
/// changes — deferred to a later release.
/// </para>
/// </summary>
public interface ISignatureLifecycleService
{
    /// <summary>
    /// Gets the current lifecycle snapshot for a signature. A signature that has never been
    /// transitioned returns the default snapshot (Active, no previous status, no notes) —
    /// this never fails for an unknown hash.
    /// </summary>
    Task<Result<SignatureLifecycleSnapshot>> GetStatusAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets lifecycle snapshots for multiple signatures in one call. Hashes with no recorded
    /// transition are included with the default (Active) snapshot.
    /// </summary>
    Task<Result<IReadOnlyDictionary<string, SignatureLifecycleSnapshot>>> GetStatusBatchAsync(
        string ownerId,
        Guid namespaceId,
        IReadOnlyList<string> signatureHashes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Transitions a signature to <paramref name="targetStatus"/>, if the transition is valid
    /// from its current status. Returns a validation failure (never throws) for a disallowed
    /// transition, e.g. re-activating directly or transitioning out of Archived.
    /// </summary>
    Task<Result<SignatureLifecycleSnapshot>> TransitionAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        SignatureLifecycleStatus targetStatus,
        string? notes,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the ordered transition history for a signature. Empty if it has never been
    /// transitioned.
    /// </summary>
    Task<Result<IReadOnlyList<SignatureLifecycleEvent>>> GetHistoryAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        CancellationToken cancellationToken = default);
}
