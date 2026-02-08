namespace ServiceHub.Core.DTOs.Responses;

/// <summary>
/// Response DTO for dead-letter operation results.
/// </summary>
/// <param name="DeadLetteredCount">The number of messages successfully moved to the dead-letter queue.</param>
/// <param name="Reason">The dead-letter reason that was applied.</param>
public sealed record DeadLetterResponse(
    int DeadLetteredCount,
    string Reason);
