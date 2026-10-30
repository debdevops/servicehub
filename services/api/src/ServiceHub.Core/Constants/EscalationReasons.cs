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

    /// <summary>Emergency stop has been on for a long time (Health Keeper).</summary>
    public const string HealthEmergencyStopOn = "HEALTH_EMERGENCY_STOP_ON";

    /// <summary>A person paused an agent and it is still paused (Health Keeper).</summary>
    public const string HealthAgentPaused = "HEALTH_AGENT_PAUSED";

    /// <summary>A recovery attempt has been executing for far longer than a cloud call takes (Health Keeper).</summary>
    public const string HealthLedgerStuck = "HEALTH_LEDGER_STUCK";

    /// <summary>A replay's observation window ended long ago and nothing closed it (Health Keeper).</summary>
    public const string HealthObservationOverdue = "HEALTH_OBSERVATION_OVERDUE";

    /// <summary>The Recovery Evidence Ledger's hash chain no longer verifies (Health Keeper).</summary>
    public const string HealthLedgerChainBroken = "HEALTH_LEDGER_CHAIN_BROKEN";

    /// <summary>A connection's last connection test failed (Health Keeper).</summary>
    public const string HealthConnectionFailing = "HEALTH_CONNECTION_FAILING";

    /// <summary>
    /// An earlier attempt to recover this message has no recorded answer (ServiceHub stopped, or lost contact with the cloud, before it
    /// could tell whether the message was sent). Nothing tries again until a person checks and says what happened.
    /// </summary>
    public const string ReplayOutcomeUnknown = "REPLAY_OUTCOME_UNKNOWN";

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
            "ServiceHub has already tried this message several times, replayed or held for a person. Trying again may fail the same way — check the cause first.",
        "RECURRENCE_CAP_AMBIGUOUS_COLLISION" =>
            "Several earlier replays look like this message, and ServiceHub can't tell which it is. A person decides.",
        "RECURRENCE_CAP_QUERY_ERROR" =>
            "ServiceHub couldn't check this message's replay history, so it stopped rather than guess. A person decides.",
        "SIGNATURE_RECENT_RESULTS_MIXED" =>
            "Some of the latest replays of this kind of failure did not hold, so it stopped replaying on its own. Check the cause, then approve the next ones yourself.",
        "SIGNATURE_RECENT_RESULTS_QUERY_ERROR" =>
            "ServiceHub couldn't read the latest results for this failure, so it stopped rather than guess. A person decides.",
        "RATE_LIMITED" or "FLEET_RATE_LIMITED" =>
            "The replay pace limit was reached. It can go now if a person says so, or wait for the next window.",
        "EMERGENCY_STOP_ACTIVE" =>
            "Emergency stop is on, so nothing replays on its own. A person decides.",
        "EMERGENCY_STOP_QUERY_ERROR" =>
            "ServiceHub couldn't check whether emergency stop is on, so it stopped rather than guess. A person decides.",
        ReplayOutcomeUnknown =>
            "ServiceHub lost contact with the cloud, or stopped, before it could record whether an earlier attempt put this message back. It may or may not have been sent, so it will not try again on its own. Check the queue, then say what you found.",
        AgentStale =>
            "This agent has stopped reporting. What it does is not happening — check the ServiceHub server.",
        AgentFailing =>
            "This agent's last cycles all failed. What it does is not happening — see its last error on the Agents page.",
        RuleStoppedItself =>
            "Fewer than half of this rule's replays stayed fixed, so it switched itself off. Look at why before turning it back on.",
        HealthEmergencyStopOn =>
            "Emergency stop has been on for a long time, so nothing replays on its own. Lift it when the cause is dealt with.",
        HealthAgentPaused =>
            "An agent is paused, so what it does is not happening. Resume it from the Agents page when you are ready.",
        HealthLedgerStuck =>
            "A recovery attempt has been running far longer than a cloud call takes. It may be stuck. ServiceHub settles it on the next start; check the cloud first.",
        HealthObservationOverdue =>
            "A replay's watching time ended long ago and it is still open, so Recovery Verification may not be closing entries. Check that agent.",
        HealthLedgerChainBroken =>
            "The recovery evidence ledger no longer verifies: a record was changed or lost. Stop relying on its history until a person has looked.",
        HealthConnectionFailing =>
            "ServiceHub could not connect to this cloud on its last test, so nothing is being read from it. Check the credential.",
        null or "" => "The agent stopped and asked a person.",
        _ => $"The agent stopped and asked a person ({code}).",
    };
}
