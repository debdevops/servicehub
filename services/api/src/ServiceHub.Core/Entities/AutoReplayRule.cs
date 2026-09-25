using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Entities;

/// <summary>
/// What ServiceHub may retry on its own, and what stops it (unit 3.6). A rule names a failure and a pace — there is no
/// rule language. Every message a rule picks still goes through the eligibility gate as an <b>automation</b> actor, so a rule
/// can never act where a signature has not earned unattended replay.
/// </summary>
/// <remarks>
/// Shape adapted from 4.0.0, which stored free-form condition JSON. 4.1.0 keeps the three things the design draws — the
/// failure (reason), the queue, and the signature it was made from — as plain columns: nothing here can grow into a DSL.
/// </remarks>
public sealed class AutoReplayRule
{
    /// <summary>Primary key.</summary>
    public long Id { get; private set; }

    /// <summary>Owner scope.</summary>
    public required string OwnerId { get; init; }

    /// <summary>A person's name for it.</summary>
    public required string Name { get; set; }

    /// <summary>The cloud it applies to. A rule never spans clouds.</summary>
    public required CloudProviderType Provider { get; init; }

    /// <summary>Match dead letters carrying this recorded reason. Null = any.</summary>
    public string? Reason { get; init; }

    /// <summary>Match this queue. Null = any.</summary>
    public string? EntityName { get; init; }

    /// <summary>The failure signature the rule was made from. Null = not tied to one.</summary>
    public string? SignatureHash { get; init; }

    /// <summary>The most messages this rule replays in an hour.</summary>
    public int MaxPerHour { get; set; } = 10;

    /// <summary>How long a message waits after it was dead-lettered before the rule touches it.</summary>
    public int WaitSeconds { get; set; } = 120;

    /// <summary>When true, each further attempt at the same message waits twice as long as the last.</summary>
    public bool BackOff { get; set; } = true;

    /// <summary>Whether the rule is on.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary><c>CircuitBreaker</c> when it stopped itself, <c>Person</c> when someone turned it off. A tripped breaker never resets itself.</summary>
    public string? DisabledReason { get; set; }

    /// <summary>In words, why it was turned off — for a breaker, the rate that tripped it.</summary>
    public string? DisabledDetail { get; set; }

    /// <summary>When it was made.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>When it last changed.</summary>
    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>The last time the gate held a matching message back and ServiceHub stopped to ask.</summary>
    public DateTimeOffset? LastAskedAt { get; set; }

    /// <summary>The gate's reason for the last hold.</summary>
    public string? LastAskedReason { get; set; }

    /// <summary>How many matching messages the gate has held for a person's decision.</summary>
    public int AskedCount { get; set; }
}
