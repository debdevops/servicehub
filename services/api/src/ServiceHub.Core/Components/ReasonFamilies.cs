using ServiceHub.Core.Constants;

namespace ServiceHub.Core.Components;

/// <summary>
/// The family label shown beside a stop reason (plan decision D7). ServiceHub's own codes in
/// <see cref="EscalationReasons"/> stay the code of record; a family is a lookup from the code and is never stored.
/// </summary>
public enum ReasonFamily
{
    /// <summary>The cloud cannot prove the fix held.</summary>
    Unverified = 0,

    /// <summary>Trust was withdrawn because recent results did not hold.</summary>
    Rejected = 1,

    /// <summary>A rule, a limit or a stop prevents acting without a person.</summary>
    PolicyBlocked = 2,

    /// <summary>It has been tried as often as is allowed.</summary>
    RetryBudgetExhausted = 3,

    /// <summary>Nothing was kept, or no safe way back exists.</summary>
    NoRecoveryPath = 4,

    /// <summary>ServiceHub does not know whether an attempt happened.</summary>
    OutcomeUnknown = 5,

    /// <summary>Later, on request: the consumer is not known to be safe against duplicates.</summary>
    NotIdempotent = 6,

    /// <summary>Later: a fan-out source with no per-subscriber way back.</summary>
    NoRetryTarget = 7,

    /// <summary>Later, on request: a dependency is known to be down.</summary>
    DependencyDown = 8,

    /// <summary>Later: the message itself cannot be processed.</summary>
    PoisonPayload = 9,

    /// <summary>Stuck in a live queue, not dead-lettered (unit 3.2).</summary>
    StalledLiveQueue = 10,

    /// <summary>Not a recovery reason at all: an agent or a rule needs attention.</summary>
    AgentHealth = 100,
}

/// <summary>
/// The crosswalk from a reason code to its family.
/// <b>Proposed mapping, waiting for the owner (build-plan unit 0.7).</b> Change a line here and nowhere else.
/// </summary>
public static class ReasonFamilies
{
    private static readonly Dictionary<string, ReasonFamily> Map = new(StringComparer.Ordinal)
    {
        ["AUTONOMY_GRANT_INSUFFICIENT"] = ReasonFamily.PolicyBlocked,
        ["AUTONOMY_SIGNATURE_HASH_MISSING"] = ReasonFamily.PolicyBlocked,
        ["AUTONOMY_GRANT_QUERY_ERROR"] = ReasonFamily.PolicyBlocked,
        ["PROVIDER_CANNOT_VERIFY_ABSENCE"] = ReasonFamily.Unverified,
        ["RECURRENCE_CAP_EXCEEDED"] = ReasonFamily.RetryBudgetExhausted,
        ["RECURRENCE_CAP_EXCEEDED_HEURISTIC"] = ReasonFamily.RetryBudgetExhausted,
        ["RECURRENCE_CAP_AMBIGUOUS_COLLISION"] = ReasonFamily.RetryBudgetExhausted,
        ["RECURRENCE_CAP_QUERY_ERROR"] = ReasonFamily.PolicyBlocked,
        ["SIGNATURE_RECENT_RESULTS_MIXED"] = ReasonFamily.Rejected,
        ["SIGNATURE_RECENT_RESULTS_QUERY_ERROR"] = ReasonFamily.PolicyBlocked,
        ["RATE_LIMITED"] = ReasonFamily.PolicyBlocked,
        ["FLEET_RATE_LIMITED"] = ReasonFamily.PolicyBlocked,
        ["EMERGENCY_STOP_ACTIVE"] = ReasonFamily.PolicyBlocked,
        ["EMERGENCY_STOP_QUERY_ERROR"] = ReasonFamily.PolicyBlocked,
        [EscalationReasons.ReplayOutcomeUnknown] = ReasonFamily.OutcomeUnknown,
        [EscalationReasons.AgentStale] = ReasonFamily.AgentHealth,
        [EscalationReasons.AgentFailing] = ReasonFamily.AgentHealth,
        [EscalationReasons.RuleStoppedItself] = ReasonFamily.Rejected,
        [EscalationReasons.HealthEmergencyStopOn] = ReasonFamily.PolicyBlocked,
        [EscalationReasons.HealthAgentPaused] = ReasonFamily.AgentHealth,
        [EscalationReasons.HealthLedgerStuck] = ReasonFamily.AgentHealth,
        [EscalationReasons.HealthObservationOverdue] = ReasonFamily.AgentHealth,
        [EscalationReasons.HealthLedgerChainBroken] = ReasonFamily.AgentHealth,
        [EscalationReasons.HealthConnectionFailing] = ReasonFamily.DependencyDown,
    };

    /// <summary>Every code that has a family. The tests check this against the codes <see cref="EscalationReasons"/> knows.</summary>
    public static IReadOnlyCollection<string> KnownCodes => Map.Keys;

    /// <summary>The family for a code, or null for a code this version does not know. Null is shown as no label, never as a guess.</summary>
    public static ReasonFamily? FamilyOf(string? code) =>
        code is not null && Map.TryGetValue(code, out var family) ? family : null;
}
