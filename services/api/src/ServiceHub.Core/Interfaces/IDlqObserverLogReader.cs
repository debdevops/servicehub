using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// Reads the infrastructure-attested DLQ observer's own durable log (`cloud-platform-infra`
/// ADR-004) — one implementation per provider that has an observer to read (AWS/DynamoDB,
/// GCP/Firestore; Azure never needs one, since <c>ProviderCapabilities.Azure.CanProveDlqAbsence</c>
/// is already <see langword="true"/> via its own uncapped peek). Read-only: this reader never
/// writes to the observer's log — only the observer's own Lambda/Cloud Function does that.
/// </summary>
public interface IDlqObserverLogReader
{
    /// <summary>The provider this reader serves.</summary>
    CloudProviderType Provider { get; }

    /// <summary>
    /// Whether the observer's log has a durable record of <paramref name="messageId"/>'s arrival —
    /// the liveness-canary check <c>DlqObserverAttestationWorker</c> uses to confirm an observer is
    /// actually live and attached (ADR-004 item 4), never a raw "does this log have any rows at all"
    /// check.
    /// </summary>
    /// <param name="ns">The namespace whose credentials to read the observer's log with.</param>
    /// <param name="observerReference">The DynamoDB table name or Firestore collection name — see
    /// <see cref="DlqObserverAttestation.ObserverReference"/>.</param>
    /// <param name="messageId">The canary's tracking ID to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<bool> HasRecordedArrivalAsync(
        Namespace ns, string observerReference, string messageId, CancellationToken cancellationToken = default);
}
