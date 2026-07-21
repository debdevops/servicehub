namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// What a specific cloud provider genuinely supports — the API-facing projection of
/// <see cref="Models.ProviderCapabilities"/>, keyed by provider name in the response map.
/// </summary>
/// <param name="SupportsMessageCounts">Whether the provider can report a real active-message count.</param>
/// <param name="SupportsManualDeadLetter">Whether a message can be dead-lettered on demand.</param>
/// <param name="SupportsPurge">Whether a single message can be permanently deleted by identity.</param>
/// <param name="SupportsScheduledMessages">Whether scheduled/delayed messages are queryable and cancellable.</param>
/// <param name="Notes">Human-readable explanation of the provider's constraints.</param>
public sealed record ProviderCapabilitiesResponse(
    bool SupportsMessageCounts,
    bool SupportsManualDeadLetter,
    bool SupportsPurge,
    bool SupportsScheduledMessages,
    string Notes);
