using ServiceHub.Core.DTOs.Responses;
using ServiceHub.Shared.Results;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// The Incident read-model (roadmap W2.1) — a projection over existing signature, recovery,
/// and playbook (anomaly/drift/correlation/prevention/replay) data, keyed by the natural
/// identity <see cref="Entities.NamespaceSignature"/> and <see cref="Entities.SignatureLifecycleState"/>
/// already use: <c>(OwnerId, NamespaceId, SignatureHash)</c>. No new store, no migration — every
/// field is composed from existing ledgers and queries, the same "no new data access layer"
/// discipline <see cref="IFailureIntelligenceCenterService"/> already applies.
/// </summary>
public interface IIncidentReadModelService
{
    /// <summary>
    /// Builds the full incident view for one signature. Returns <see cref="ErrorType.NotFound"/>
    /// when the signature has never been observed (no <see cref="Entities.NamespaceSignature"/>
    /// row) and has no recovery or playbook activity recorded against it either — i.e. there is
    /// nothing to show, not merely that the signature is not currently clustered (mirrors
    /// <c>DlqHistoryController.GetSignatureDetail</c>'s persisted-fallback behavior for that
    /// distinction).
    /// </summary>
    Task<Result<IncidentDetailResponse>> GetIncidentAsync(
        string ownerId,
        Guid namespaceId,
        string signatureHash,
        CancellationToken cancellationToken = default);
}
