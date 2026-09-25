using ServiceHub.Core.Enums;

namespace ServiceHub.Core.Models;

/// <summary>What a rule looks like on screen — the rule plus what actually happened under it (never a stored counter).</summary>
public sealed record RuleView(
    long Id, string Name, CloudProviderType Provider, string? Reason, string? EntityName, string? SignatureHash,
    int MaxPerHour, int WaitSeconds, bool BackOff, bool Enabled, string? DisabledReason, string? DisabledDetail,
    DateTimeOffset CreatedAt, DateTimeOffset? UpdatedAt, DateTimeOffset? LastAskedAt, string? LastAskedReason, int AskedCount,
    int Replayed, DateTimeOffset? LastReplayedAt, int VerifiedOutcomes, int StayedFixed, int SampleSize, double SuccessFloor);

/// <summary>A held-back message in a rule test, grouped by the gate's reason.</summary>
public sealed record RuleTestHold(string ReasonCode, string Remedy, int Count);

/// <summary>
/// "Tested on the last 7 days: it would have matched N; of those still waiting, K would pass the checks today and the rest would be held back."
/// Messages already gone from the queue count as matched but are neither "would run" nor "held back": there is nothing left to decide.
/// </summary>
public sealed record RuleTest(int Days, int Matched, int StillWaiting, int WouldRun, int HeldBack, IReadOnlyList<RuleTestHold> Holds);

/// <summary>A failure a rule can be created from: one signature, in words first.</summary>
public sealed record RuleSource(string SignatureHash, string Reason, string EntityName, int Messages, string? ExampleError);
