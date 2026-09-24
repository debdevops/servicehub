using ServiceHub.Core.Models;

namespace ServiceHub.Core.Interfaces;

/// <summary>
/// One named background capability. <b>An agent is one file plus one registration line</b>, and
/// that is the whole extensibility story (PLAN 1.5; Gate 4 tests it).
/// </summary>
/// <remarks>
/// An agent implements a single cycle and nothing else. The host owns the loop, the cadence, the
/// heartbeat, the error handling, the pause and the authority enforcement — so an agent never
/// re-implements any of them, and every agent behaves identically when things go wrong. That is
/// the one thing 4.0.0's twenty <c>BackgroundService</c> workers could not promise.
/// </remarks>
public interface IAgent
{
    /// <summary>How this agent describes itself. Must be constant for the process's lifetime.</summary>
    AgentDescriptor Descriptor { get; }

    /// <summary>
    /// Do one unit of work. Return normally to report the cycle; throw to report a failure.
    /// </summary>
    /// <remarks>
    /// Must honour <paramref name="ct"/> promptly — shutdown waits for it. Must be safe to run
    /// after an abrupt restart: agents are independent loops over durable state, and that property
    /// is precisely why restarts are safe.
    /// </remarks>
    Task<AgentCycleResult> ExecuteCycleAsync(CancellationToken ct);
}
