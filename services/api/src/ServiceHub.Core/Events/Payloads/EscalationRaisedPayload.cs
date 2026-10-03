namespace ServiceHub.Core.Events.Payloads;

/// <summary>
/// What an escalation is (unit 5.2), for the channels that carry it beyond the app (Slack, Teams, a webhook). The
/// <see cref="ReasonCode"/> is the valuable part — the gate's own code, never flattened to a generic string.
/// </summary>
public sealed record EscalationRaisedPayload
{
    /// <summary><c>approval</c> — a replay waits for a person · <c>agent</c> — an agent has stopped reporting.</summary>
    public required string Kind { get; init; }

    /// <summary>The ledger entry that holds it (approvals), or null.</summary>
    public Guid? EntryId { get; init; }

    /// <summary>The agent (agent escalations), or null.</summary>
    public string? AgentId { get; init; }

    /// <summary>Where.</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>The namespace's name at the time.</summary>
    public string? NamespaceName { get; init; }

    /// <summary>azure · aws · gcp, or null.</summary>
    public string? Provider { get; init; }

    /// <summary>The queue or subscription.</summary>
    public string? Entity { get; init; }

    /// <summary>The gate's reason code, e.g. <c>PROVIDER_CANNOT_VERIFY_ABSENCE</c>, or <c>AGENT_STALE</c>.</summary>
    public required string ReasonCode { get; init; }

    /// <summary>The same thing in one plain sentence, with what to do.</summary>
    public required string Reason { get; init; }

    /// <summary>When.</summary>
    public required DateTimeOffset RaisedAtUtc { get; init; }
}
