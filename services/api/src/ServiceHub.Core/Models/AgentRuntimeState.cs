using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>
/// What the host knows about a registered agent right now. Held in memory, rebuilt on restart —
/// 4.0.0 kept worker heartbeats in memory and that was the right call (no table, cannot drift).
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
    bool IsPaused);
