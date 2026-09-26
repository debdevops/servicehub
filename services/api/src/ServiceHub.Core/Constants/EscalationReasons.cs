namespace ServiceHub.Core.Constants;

/// <summary>
/// Why an agent stopped and asked, in one plain sentence per reason code, with what to do (units 5.2–5.10). One source:
/// the bell, the Needs-you strip, the Approve modal and the Slack/Teams messages all read the same words, and the code
/// travels beside them so nothing is flattened.
/// </summary>
public static class EscalationReasons
{
    /// <summary>An agent has stopped reporting (unit 5.6).</summary>
    public const string AgentStale = "AGENT_STALE";

    /// <summary>An agent's cycles keep failing (unit 5.6).</summary>
    public const string AgentFailing = "AGENT_FAILING";

    /// <summary>A rule's circuit breaker switched it off (unit 3.6); it waits until a person looks.</summary>
    public const string RuleStoppedItself = "RULE_CIRCUIT_BREAKER";

    /// <summary>The sentence for a code. An unknown code still gets an honest sentence, never a blank.</summary>
    public static string Describe(string? code) => code switch
    {
        "AUTONOMY_GRANT_INSUFFICIENT" =>
            "This kind of failure hasn't earned automatic replay yet (it needs 10 verified fixes at 95% or better), so a person decides.",
        "AUTONOMY_SIGNATURE_HASH_MISSING" =>
            "ServiceHub couldn't tell which kind of failure this is, so it won't replay it on its own. A person decides.",
        "AUTONOMY_GRANT_QUERY_ERROR" =>
            "ServiceHub couldn't read what this failure has earned, so it stopped rather than guess. A person decides.",
        "PROVIDER_CANNOT_VERIFY_ABSENCE" =>
            "This cloud can't prove a replayed message stayed fixed, so ServiceHub never replays here on its own. A person decides.",
        "RECURRENCE_CAP_EXCEEDED" or "RECURRENCE_CAP_EXCEEDED_HEURISTIC" =>
            "This message has come back after replaying before. Replaying it again may fail the same way — check the cause first.",
        "RECURRENCE_CAP_AMBIGUOUS_COLLISION" =>
            "Several earlier replays look like this message, and ServiceHub can't tell which it is. A person decides.",
        "RECURRENCE_CAP_QUERY_ERROR" =>
            "ServiceHub couldn't check this message's replay history, so it stopped rather than guess. A person decides.",
        "RATE_LIMITED" or "FLEET_RATE_LIMITED" =>
            "The replay pace limit was reached. It can go now if a person says so, or wait for the next window.",
        "EMERGENCY_STOP_ACTIVE" =>
            "Emergency stop is on, so nothing replays on its own. A person decides.",
        "EMERGENCY_STOP_QUERY_ERROR" =>
            "ServiceHub couldn't check whether emergency stop is on, so it stopped rather than guess. A person decides.",
        AgentStale =>
            "This agent has stopped reporting. What it does is not happening — check the ServiceHub server.",
        AgentFailing =>
            "This agent's last cycles all failed. What it does is not happening — see its last error on the Agents page.",
        RuleStoppedItself =>
            "Fewer than half of this rule's replays stayed fixed, so it switched itself off. Look at why before turning it back on.",
        null or "" => "The agent stopped and asked a person.",
        _ => $"The agent stopped and asked a person ({code}).",
    };
}
