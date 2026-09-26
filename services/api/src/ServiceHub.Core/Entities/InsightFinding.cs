namespace ServiceHub.Core.Entities;

/// <summary>
/// One thing an Insights engine noticed (unit 6.18) — the only table the Insights tab reads. Written by the Insights agents,
/// never by a person, and never acted on: a finding does not replay, open a rule or change authority.
/// </summary>
public sealed class InsightFinding
{
    /// <summary>Row id.</summary>
    public long Id { get; private set; }

    /// <summary>Whose.</summary>
    public required string OwnerId { get; init; }

    /// <summary><c>anomaly</c>, <c>backlog</c>, <c>correlation</c> or <c>narration</c>.</summary>
    public required string Kind { get; init; }

    /// <summary>What makes two cycles' findings the same finding: kind, namespace and queue (or the correlated members).</summary>
    public required string Key { get; init; }

    /// <summary>The namespace, when the finding is about one.</summary>
    public Guid? NamespaceId { get; init; }

    /// <summary>The queue or subscription, when the finding is about one.</summary>
    public string? EntityName { get; init; }

    /// <summary>10–100, from the engine.</summary>
    public int Severity { get; set; }

    /// <summary>The engine's own sentence. For a narration, templated text — shown as a suggestion (R3).</summary>
    public required string What { get; set; }

    /// <summary>The numbers behind it, as the engine computed them.</summary>
    public string? MetricsJson { get; set; }

    /// <summary>When it was first noticed.</summary>
    public required DateTimeOffset FirstSeenAt { get; init; }

    /// <summary>When a cycle last found it still true.</summary>
    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>When a cycle stopped finding it; null while it is still true.</summary>
    public DateTimeOffset? ClearedAt { get; set; }
}
