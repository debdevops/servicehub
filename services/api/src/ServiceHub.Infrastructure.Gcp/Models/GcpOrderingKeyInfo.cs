namespace ServiceHub.Infrastructure.Gcp.Models;

/// <summary>
/// Describes the delivery state of a GCP Pub/Sub ordering key.
/// Used to detect stalled ordering keys — a common GCP-specific debugging challenge
/// where a single failing message blocks all subsequent messages with the same key.
/// </summary>
/// <param name="OrderingKey">The ordering key value.</param>
/// <param name="DeliveryAttempts">
/// Number of times a message with this ordering key has been attempted without acknowledgement.
/// </param>
/// <param name="IsStalled">
/// <see langword="true"/> when messages with this ordering key are not being acknowledged,
/// likely because an in-order message is failing and blocking subsequent ones.
/// </param>
public sealed record GcpOrderingKeyInfo(
    string OrderingKey,
    int DeliveryAttempts,
    bool IsStalled);
