namespace ServiceHub.Core.Models;

/// <summary>
/// Configuration options for the ServiceHub reasoning-companion HTTP client (roadmap §7, W5).
/// Bound from the "ReasoningAgent" section of appsettings.json. Mirrors <see cref="AIServiceOptions"/>'s
/// shape and disabled-by-default discipline — deliberately a separate options type, not a reuse of
/// it, since the two services have independent enable switches and lifecycles.
/// </summary>
public sealed class ReasoningAgentOptions
{
    /// <summary>Section name in configuration.</summary>
    public const string SectionName = "ReasoningAgent";

    /// <summary>
    /// Whether the reasoning-companion integration is enabled. Defaults to false — an operator
    /// must opt in, matching <see cref="AIServiceOptions.Enabled"/>'s discipline. Even when true,
    /// the companion itself may still produce no proposals if its own <c>OLLAMA_HOST</c> is unset —
    /// see <c>services/agent/README.md</c>.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>Base URL of the reasoning-companion service (e.g. http://servicehub-agent:8010).</summary>
    public string ServiceUrl { get; set; } = string.Empty;

    /// <summary>Default HTTP client timeout, in seconds. Higher than <see cref="AIServiceOptions.TimeoutSeconds"/>'s
    /// default — a local-LLM inference call is slower than a clustering request.</summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>How often <c>ReasoningCompanionWorker</c> runs a sweep, in minutes.</summary>
    public int SweepIntervalMinutes { get; set; } = 60;

    /// <summary>Maximum number of attention-queue signatures considered per owner, per sweep —
    /// bounds both the evidence sent off-process and the number of Playbook entries one sweep
    /// can create.</summary>
    public int MaxSignaturesPerSweep { get; set; } = 3;
}
