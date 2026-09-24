namespace ServiceHub.Core.Enums;

/// <summary>
/// How much an agent is permitted to do on its own.
/// </summary>
/// <remarks>
/// This is a <b>declaration the host checks</b>, not documentation. An agent declared
/// <see cref="Observes"/> or <see cref="Proposes"/> that reports having changed something is
/// treated by <c>AgentHost</c> as a contract violation — the cycle is failed and the agent is
/// marked failing — rather than having its report quietly recorded. Raising an agent's authority is
/// a decision that needs the owner (rule R8: observability may grow without limit; authority may
/// not grow at all).
/// </remarks>
public enum AgentAuthority
{
    /// <summary>Reads and records. Cannot mutate anything, anywhere.</summary>
    Observes = 0,

    /// <summary>Produces a recommendation a human acts on. Cannot execute it.</summary>
    Proposes = 1,

    /// <summary>Executes, but only against work a human has approved.</summary>
    ActsWithApproval = 2,

    /// <summary>Executes without asking, within the eligibility gate's limits.</summary>
    ActsAutonomously = 3
}
