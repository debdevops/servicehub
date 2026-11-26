using ServiceHub.Core.Constants;
using ServiceHub.Core.Entities;
using ServiceHub.Core.Enums;
using ServiceHub.Core.Events;
using ServiceHub.Core.Interfaces;
using ServiceHub.Core.Models;

namespace ServiceHub.Infrastructure.Agents;

/// <summary>One thing the Health Keeper found wrong. <see cref="Key"/> is stable while the problem lasts, so it is announced once per episode.</summary>
/// <param name="Key">Code plus what it is about (an agent id, a connection id), unique per owner.</param>
/// <param name="Code">An <see cref="EscalationReasons"/> health code.</param>
/// <param name="Reason">One plain sentence, specific to this finding.</param>
/// <param name="Severity">Warning, Error or Critical.</param>
/// <param name="NamespaceId">The connection it is about, when it is about one (so an owner channel can route it).</param>
/// <param name="NamespaceName">Its name.</param>
internal sealed record HealthFinding(string Key, string Code, string Reason, EventSeverity Severity, Guid? NamespaceId = null, string? NamespaceName = null);

/// <summary>Everything the checks look at for one owner. Read once per cycle; the checks themselves touch nothing.</summary>
/// <param name="EmergencyStop">The owner's emergency-stop state.</param>
/// <param name="Agents">Every agent's state (agents serve all owners).</param>
/// <param name="Open">The owner's recovery entries that are not terminal.</param>
/// <param name="ChainChecked">Whether the hash chain was verified this cycle (it is not verified every time).</param>
/// <param name="ChainValid">The verification result when it was checked.</param>
/// <param name="Connections">The owner's active connections.</param>
internal sealed record HealthSnapshot(
    EmergencyStopState EmergencyStop,
    IReadOnlyList<AgentRuntimeState> Agents,
    IReadOnlyList<RecoveryLedgerEntry> Open,
    bool ChainChecked,
    bool ChainValid,
    IReadOnlyList<Namespace> Connections);

/// <summary>
/// The Health Keeper's rules. Deterministic and side-effect free: the same snapshot always gives the same findings, so each rule is tested
/// on its own. The thresholds are fixed, not settings, so what counts as unhealthy never depends on a configuration someone forgot.
/// </summary>
internal static class HealthChecks
{
    /// <summary>Emergency stop that has been on longer than this is worth a reminder.</summary>
    internal static readonly TimeSpan EmergencyStopReminder = TimeSpan.FromHours(1);

    /// <summary>A cloud call does not take this long. An attempt still Executing after it is stuck.</summary>
    internal static readonly TimeSpan ExecutingTooLong = TimeSpan.FromMinutes(15);

    /// <summary>An observation window that ended this long ago and is still open means verification is not closing entries.</summary>
    internal static readonly TimeSpan ObservationOverdueBy = TimeSpan.FromHours(2);

    /// <summary>The agent that runs these checks; a stale or failing Health Keeper is the watchdog's to report, not its own.</summary>
    internal const string OwnAgentId = "health";

    internal static IReadOnlyList<HealthFinding> Evaluate(HealthSnapshot s, DateTimeOffset now)
    {
        var found = new List<HealthFinding>();

        if (s.EmergencyStop.Active && s.EmergencyStop.At is { } since && now - since >= EmergencyStopReminder)
        {
            found.Add(new HealthFinding(EscalationReasons.HealthEmergencyStopOn, EscalationReasons.HealthEmergencyStopOn,
                $"Emergency stop has been on for {Words(now - since)}{(string.IsNullOrWhiteSpace(s.EmergencyStop.Reason) ? "" : $" ({s.EmergencyStop.Reason})")}. Nothing replays on its own until it is lifted.",
                EventSeverity.Warning));
        }

        foreach (var a in s.Agents.Where(a => a.IsPaused && a.Descriptor.Id != OwnAgentId))
        {
            found.Add(new HealthFinding($"{EscalationReasons.HealthAgentPaused}:{a.Descriptor.Id}", EscalationReasons.HealthAgentPaused,
                $"{a.Descriptor.Name} is paused, so what it does is not happening.", EventSeverity.Warning));
        }

        var stuck = s.Open.Where(e => e.State == RecoveryEntryState.Executing && now - e.BegunAt > ExecutingTooLong).ToList();
        if (stuck.Count > 0)
        {
            found.Add(new HealthFinding(EscalationReasons.HealthLedgerStuck, EscalationReasons.HealthLedgerStuck,
                $"{Plural(stuck.Count, "recovery attempt")} {(stuck.Count == 1 ? "has" : "have")} been running for over {Words(ExecutingTooLong)} (the oldest for {Words(now - stuck.Min(e => e.BegunAt))}). {(stuck.Count == 1 ? "It" : "They")} may be stuck.",
                EventSeverity.Error));
        }

        var overdue = s.Open.Where(e => e.State == RecoveryEntryState.Observing && e.ObservationWindowEndsAt is { } end && now - end > ObservationOverdueBy).ToList();
        if (overdue.Count > 0)
        {
            found.Add(new HealthFinding(EscalationReasons.HealthObservationOverdue, EscalationReasons.HealthObservationOverdue,
                $"{Plural(overdue.Count, "replay")} should have been closed over {Words(ObservationOverdueBy)} ago and {(overdue.Count == 1 ? "is" : "are")} still being watched. Recovery Verification may not be running.",
                EventSeverity.Error));
        }

        if (s.ChainChecked && !s.ChainValid)
        {
            found.Add(new HealthFinding(EscalationReasons.HealthLedgerChainBroken, EscalationReasons.HealthLedgerChainBroken,
                "The recovery evidence ledger no longer verifies: a record was changed or lost.", EventSeverity.Critical));
        }

        foreach (var n in s.Connections.Where(n => n.LastConnectionTestSucceeded == false))
        {
            var name = n.DisplayName ?? n.Name;
            found.Add(new HealthFinding($"{EscalationReasons.HealthConnectionFailing}:{n.Id}", EscalationReasons.HealthConnectionFailing,
                $"{name}: the last connection test failed, so nothing is being read from it.", EventSeverity.Error, n.Id, name));
        }

        return found;
    }

    private static string Plural(int n, string noun) => n == 1 ? $"1 {noun}" : $"{n} {noun}s";

    private static string Words(TimeSpan t) =>
        t.TotalDays >= 1 ? $"{(int)t.TotalDays} day{((int)t.TotalDays == 1 ? "" : "s")}"
        : t.TotalHours >= 1 ? $"{(int)t.TotalHours} hour{((int)t.TotalHours == 1 ? "" : "s")}"
        : $"{Math.Max(1, (int)t.TotalMinutes)} minute{((int)t.TotalMinutes <= 1 ? "" : "s")}";
}
