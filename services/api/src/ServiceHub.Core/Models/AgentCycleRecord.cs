namespace ServiceHub.Core.Models;

/// <summary>
/// One finished cycle of one agent, as the host saw it (unit 4.4). Kept in memory for the recent past only: a restart
/// starts the list again, which is the truth — nothing is remembered that the running process did not see.
/// </summary>
/// <param name="RunUtc">When the cycle started.</param>
/// <param name="Result">What it reported; null when it failed.</param>
/// <param name="Failure">Why it failed; null when it succeeded.</param>
public sealed record AgentCycleRecord(DateTimeOffset RunUtc, AgentCycleResult? Result, string? Failure);
