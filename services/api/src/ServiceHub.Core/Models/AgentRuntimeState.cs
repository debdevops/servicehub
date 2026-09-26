using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// What the host knows about a registered agent right now. Held in memory, rebuilt on restart —
/// no table, so it cannot drift.
/// </summary>
/// <param name="Descriptor">The agent's own description of itself.</param>
/// <param name="Health">Derived from recent cycles, never self-reported.</param>
/// <param name="LastRunUtc">When a cycle last completed, successfully or not.</param>
/// <param name="LastResult">What that cycle reported, if it completed.</param>
/// <param name="LastFailure">
/// Why the last cycle failed, if it did. Redacted before it leaves the process.
/// </param>
/// <param name="ConsecutiveFailures">Reset to zero by any successful cycle.</param>
/// <param name="IsPaused">
/// A human has paused it. The loop keeps running and the agent <b>will not act</b> — the wording
/// matters, because "stopped" would be untrue.
/// </param>
public sealed record AgentRuntimeState(
    AgentDescriptor Descriptor,
    AgentHealth Health,
    DateTimeOffset? LastRunUtc,
    AgentCycleResult? LastResult,
    string? LastFailure,
    int ConsecutiveFailures,
    bool IsPaused)
{
    /// <summary>Failed cycles in a row at which an agent becomes pending work (unit 5.6).</summary>
    public const int FailingThreshold = 3;

    /// <summary>
    /// True when it has not started a cycle for three cadences (at least a minute) past its last one — the one rule the
    /// Agents page and the bell share, so they can never disagree. A paused agent is never late: it was told to wait.
    /// </summary>
    public bool IsLate(DateTimeOffset now) =>
        !IsPaused && LastRunUtc is { } last
        && now - last > TimeSpan.FromTicks(Math.Max(Descriptor.Cadence.Ticks * 3, TimeSpan.FromMinutes(1).Ticks)) + Descriptor.Cadence;

    /// <summary>True when it needs a person: late, or failing cycle after cycle (and not paused).</summary>
    public bool NeedsAPerson(DateTimeOffset now) => !IsPaused && (IsLate(now) || ConsecutiveFailures >= FailingThreshold);
}
