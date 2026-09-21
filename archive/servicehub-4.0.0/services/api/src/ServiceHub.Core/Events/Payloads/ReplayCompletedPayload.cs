namespace ServiceHub.Core.Events.Payloads;

/// <summary>
/// Payload for <see cref="EventTypes.ReplayCompleted"/>.
/// Raised after a DLQ message replay attempt finishes, regardless of outcome.
/// Subscribers can use <see cref="Success"/> to branch handling.
/// </summary>
public sealed record ReplayCompletedPayload
{
    /// <summary>Internal DLQ record primary key of the replayed message.</summary>
    public required long DlqRecordId { get; init; }

    /// <summary>Service Bus (or provider-equivalent) message identifier that was replayed.</summary>
    public required string MessageId { get; init; }

    /// <summary>Identifier of the namespace the replay was issued against.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Name of the source entity (queue or subscription) the message was replayed to.</summary>
    public required string EntityName { get; init; }

    /// <summary>
    /// <c>true</c> if the replay succeeded; <c>false</c> if it failed.
    /// </summary>
    public required bool Success { get; init; }

    /// <summary>
    /// Error message if the replay failed; null on success.
    /// Must not contain raw message bodies or secrets.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Identifier of the auto-replay rule that triggered this replay.
    /// Null for manual replays initiated by a user through the API.
    /// </summary>
    public Guid? TriggeredByRuleId { get; init; }

    /// <summary>UTC timestamp when the replay attempt completed.</summary>
    public required DateTimeOffset CompletedAtUtc { get; init; }
}
