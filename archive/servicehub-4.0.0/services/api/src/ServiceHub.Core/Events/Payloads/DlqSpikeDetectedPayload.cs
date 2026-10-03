namespace ServiceHub.Core.Events.Payloads;

/// <summary>
/// Payload for <see cref="EventTypes.DlqSpikeDetected"/>.
/// Raised when the DLQ monitor detects a volume of new messages that exceeds
/// the configured spike threshold in a single scan cycle.
/// Future subscribers include the WebhookNotifier (Phase 3 migration),
/// Alert Engine, and SSE push pipeline.
/// </summary>
public sealed record DlqSpikeDetectedPayload
{
    /// <summary>Identifier of the namespace where the spike was detected.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Display name of the namespace where the spike was detected.</summary>
    public required string NamespaceName { get; init; }

    /// <summary>Number of new DLQ messages detected in the triggering scan cycle.</summary>
    public required int NewMessageCount { get; init; }

    /// <summary>UTC timestamp when the scan cycle that detected the spike completed.</summary>
    public required DateTimeOffset DetectedAtUtc { get; init; }
}
