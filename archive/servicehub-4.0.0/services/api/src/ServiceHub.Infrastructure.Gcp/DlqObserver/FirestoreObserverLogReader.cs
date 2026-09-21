using Google.Cloud.Firestore;
using Microsoft.Extensions.Logging;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Interfaces;
using ServiceHub.Infrastructure.Security;

namespace ServiceHub.Infrastructure.Gcp.DlqObserver;

/// <summary>
/// Reads the GCP DLQ observer's Firestore log (`terraform/modules/gcp/dlq-observer`,
/// `cloud-platform-infra` ADR-004). One document lookup by <c>messageId</c> — the observer's
/// Cloud Function writes exactly one document per message, keyed the same way, so a single point
/// lookup is sufficient; no collection query.
/// </summary>
public sealed class FirestoreObserverLogReader : IDlqObserverLogReader
{
    private readonly IGcpClientFactory _clientFactory;
    private readonly ILogger<FirestoreObserverLogReader> _logger;

    /// <summary>Initialises a new instance of <see cref="FirestoreObserverLogReader"/>.</summary>
    public FirestoreObserverLogReader(IGcpClientFactory clientFactory, ILogger<FirestoreObserverLogReader> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public CloudProviderType Provider => CloudProviderType.Gcp;

    /// <inheritdoc />
    public async Task<bool> HasRecordedArrivalAsync(
        Namespace ns, string observerReference, string messageId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ns);
        ArgumentException.ThrowIfNullOrWhiteSpace(observerReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageId);

        try
        {
            var db = await _clientFactory.GetFirestoreDbAsync(ns, cancellationToken).ConfigureAwait(false);
            var snapshot = await db.Collection(observerReference).Document(messageId)
                .GetSnapshotAsync(cancellationToken).ConfigureAwait(false);

            return snapshot.Exists;
        }
        catch (Exception ex)
        {
            // A misconfigured ObserverReference (wrong collection name, missing Firestore
            // database) or a transient RPC failure — either way, fail closed rather than let an
            // exception here be mistaken for "confirmed."
            _logger.LogWarning(ex,
                "DLQ observer Firestore read failed for collection {Collection} in namespace {NamespaceId} — " +
                "check DlqObserverAttestation.ObserverReference",
                LogRedactor.SanitiseForLog(observerReference), ns.Id);
            return false;
        }
    }
}
