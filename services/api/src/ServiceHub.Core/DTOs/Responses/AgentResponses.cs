namespace ServiceHub.Core.DTOs.Responses;

/// <summary>One agent as the Agents screen draws it (unit 4.4) — its descriptor plus what the running process saw.</summary>
/// <param name="Id">Stable id.</param>
/// <param name="Name">What a person calls it.</param>
/// <param name="Purpose">One hand-written sentence.</param>
/// <param name="Kind">watch · decide · act · maintain.</param>
/// <param name="Authority">observes · proposes · actsWithApproval · actsAutonomously.</param>
/// <param name="CanAct">True when it can change something outside ServiceHub.</param>
/// <param name="CadenceSeconds">How often a cycle runs.</param>
/// <param name="Notes">Per-cloud differences in plain words.</param>
/// <param name="May">What it may do.</param>
/// <param name="MayNot">What it never does.</param>
/// <param name="Health">healthy · degraded · failing · paused · unknown — from its cycles, never self-reported.</param>
/// <param name="Late">True when it has not completed a cycle for well over its cadence.</param>
/// <param name="IsPaused">A person paused it: it will not run its cycles until resumed.</param>
/// <param name="LastRunUtc">When its last cycle started.</param>
/// <param name="LastResult">What its last successful cycle reported.</param>
/// <param name="LastFailure">Why its last cycle failed, if it did.</param>
/// <param name="ConsecutiveFailures">Failed cycles in a row.</param>
public sealed record AgentResponse(
    string Id, string Name, string Purpose, string Kind, string Authority, bool CanAct, double CadenceSeconds, string? Notes,
    IReadOnlyList<string> May, IReadOnlyList<string> MayNot, string Health, bool Late, bool IsPaused,
    DateTimeOffset? LastRunUtc, AgentCycleResponse? LastResult, string? LastFailure, int ConsecutiveFailures);

/// <summary>What one cycle reported.</summary>
public sealed record AgentCycleResponse(int Examined, int Changed, string Summary, bool Degraded);

/// <summary>One line of an agent's timeline.</summary>
/// <param name="At">When.</param>
/// <param name="Source">cycle (seen by this process since it started) · ledger (recorded evidence) · audit (a person paused or resumed it).</param>
/// <param name="Kind">For a cycle: idle · acted · looked · degraded · failed. For the ledger: the event type. For the audit: paused · resumed.</param>
/// <param name="Text">What happened, in words.</param>
/// <param name="By">Who, for a pause or resume.</param>
public sealed record AgentActivityItem(DateTimeOffset At, string Source, string Kind, string Text, string? By);

/// <summary>An agent's timeline, newest first. Cycles are only what this process saw since it started — it says so.</summary>
public sealed record AgentActivityResponse(string AgentId, DateTimeOffset? CyclesSinceUtc, IReadOnlyList<AgentActivityItem> Items);
