namespace ServiceHub.Core.Events.Payloads;

/// <summary>
/// Payload for <see cref="EventTypes.RuleMatched"/>.
/// Raised by the auto-replay rule engine when a rule matches a DLQ message
/// and an action is executed.
/// </summary>
public sealed record RuleMatchedPayload
{
    /// <summary>Identifier of the auto-replay rule that matched.</summary>
    public required Guid RuleId { get; init; }

    /// <summary>Display name of the matched rule.</summary>
    public required string RuleName { get; init; }

    /// <summary>Internal DLQ record primary key of the message the rule matched against.</summary>
    public required long DlqRecordId { get; init; }

    /// <summary>Service Bus (or provider-equivalent) message identifier.</summary>
    public required string MessageId { get; init; }

    /// <summary>Identifier of the namespace containing the matched message.</summary>
    public required Guid NamespaceId { get; init; }

    /// <summary>Name of the entity (queue or subscription) that hosts the matched message.</summary>
    public required string EntityName { get; init; }

    /// <summary>
    /// Action type the rule engine executed upon match
    /// (e.g. "Replay", "Archive", "Discard").
    /// </summary>
    public required string ActionType { get; init; }

    /// <summary>UTC timestamp when the rule match was evaluated.</summary>
    public required DateTimeOffset MatchedAtUtc { get; init; }
}
