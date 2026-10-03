namespace ServiceHub.Core.Enums;

/// <summary>
/// How an agent is doing, derived from its recent cycles — never self-reported optimism.
/// </summary>
public enum AgentHealth
{
    /// <summary>Running on cadence, last cycle succeeded.</summary>
    Healthy = 0,

    /// <summary>Running, but the last cycle reported a problem it recovered from.</summary>
    Degraded = 1,

    /// <summary>The last cycle failed, or the agent has missed its cadence.</summary>
    Failing = 2,

    /// <summary>A human has paused it. The loop still runs; the agent will not act.</summary>
    Paused = 3,

    /// <summary>Registered but has not completed a cycle yet.</summary>
    Unknown = 4
}
