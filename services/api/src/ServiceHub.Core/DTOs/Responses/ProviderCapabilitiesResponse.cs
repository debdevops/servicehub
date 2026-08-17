namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// What a specific cloud provider genuinely supports — the API-facing projection of
/// <see cref="Models.ProviderCapabilities"/>, keyed by provider name in the response map.
/// </summary>
/// <param name="SupportsMessageCounts">Whether the provider can report a real active-message count.</param>
/// <param name="SupportsManualDeadLetter">Whether a message can be dead-lettered on demand.</param>
/// <param name="SupportsPurge">Whether a single message can be permanently deleted by identity.</param>
/// <param name="SupportsScheduledMessages">Whether scheduled/delayed messages are queryable and cancellable.</param>
/// <param name="SupportsRepeatablePeek">Whether peek is safe to call on a short, repeating interval (auto-refresh, Live Tail) without accumulating toward the entity's own redelivery limits.</param>
/// <param name="Notes">Human-readable explanation of the provider's constraints.</param>
/// <param name="SupportsRecoveryMarker">Whether the provider's message envelope can carry the
/// recovery-tracking marker stamped on replay.</param>
/// <param name="CanProveDlqAbsence">Whether ServiceHub can establish that a replayed message did
/// not return to the DLQ — the fact that determines whether this provider can ever earn
/// unattended (L4/L5) auto-replay. <see langword="false"/> means auto-replay is bounded to
/// human-approved (L3) execution regardless of trust-evidence accumulated so far.</param>
public sealed record ProviderCapabilitiesResponse(
    bool SupportsMessageCounts,
    bool SupportsManualDeadLetter,
    bool SupportsPurge,
    bool SupportsScheduledMessages,
    bool SupportsRepeatablePeek,
    string Notes,
    bool SupportsRecoveryMarker,
    bool CanProveDlqAbsence);
