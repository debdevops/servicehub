using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// The single source of truth for which agents exist. <b>The Agents screen renders this and
/// nothing else</b> — so it cannot list an agent that is not running (ADR-0014 D7).
/// </summary>
public interface IAgentRegistry
{
    /// <summary>Every registered agent's current state, in a stable order.</summary>
    IReadOnlyList<AgentRuntimeState> All();

    /// <summary>One agent's state, or <c>null</c> when no agent has that id.</summary>
    AgentRuntimeState? StateOf(string agentId);

    /// <summary>
    /// Pause or resume an agent. A paused agent's loop keeps running and its cycles are skipped:
    /// it <b>will not act</b>. Returns false when no agent has that id.
    /// </summary>
    bool SetPaused(string agentId, bool paused);
}
